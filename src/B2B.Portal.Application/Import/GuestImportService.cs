using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.Services;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Application.Import;

/// <summary>
/// Excel-Gäste-Import (Phase 4 des ursprünglichen 5-Phasen-Plans): eine Zeile pro Gast, die
/// Spaltenüberschriften werden über ein konfigurierbares Mapping auf Gast-Stammdaten
/// (Mail/DisplayName), Ziel-Workload/Szenario und freie ScenarioResourceRule.Fields-
/// Schlüssel abgebildet. Die technische xlsx-Lesearbeit übernimmt ISpreadsheetReader
/// (Infrastructure-Adapter, ClosedXML) — dieser Service kennt nur Rohwert-Zeilen, damit
/// Application paketfrei bleibt (dieselbe Trennung wie bei allen anderen technischen
/// Adaptern). PreviewAsync und CommitAsync teilen sich denselben Matching-Code
/// (ProcessRowAsync), um Preview/Commit-Drift auszuschließen — "gather facts, then
/// evaluate", dasselbe Muster wie LifecycleService.EvaluateDeletionAsync.
/// E-Mail ist der eindeutige Gast-Schlüssel. Ändern sich bei einer bereits bekannten Mail
/// andere Felder, wird der Datensatz überschrieben (auditiert), und für jede bestehende
/// aktive Zuweisung des Gasts in einem ANDEREN Workload wird ein ReviewItem mit Reason
/// angelegt — der jeweilige Workload-Owner soll manuell prüfen, ob die Zuweisung durch die
/// geänderten Daten noch gültig ist (kein automatisches Entziehen, das bliebe
/// LifecycleService/Governance Core vorbehalten, Anhang A Regel 3).
/// </summary>
public sealed class GuestImportService(
    ISpreadsheetReader spreadsheetReader,
    IWorkloadRepository workloadRepository,
    IWorkloadScenarioRepository scenarioRepository,
    IGuestAccountRepository guestRepository,
    IAssignmentRepository assignmentRepository,
    IReviewRepository reviewRepository,
    GrantWorkloadRoleCommandHandler grantWorkloadRoleHandler,
    AuditService auditService)
{
    // Fester Marker statt einer echten ReviewDefinition — ReviewInstance.ReviewDefinitionId
    // wird laut bestehendem Muster (siehe StartReviewHandler) nicht per Fremdschlüssel
    // aufgelöst, sondern nur als Kennzeichnung gespeichert.
    private static readonly Guid GuestImportReviewDefinitionId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public GuestImportInspectResult Inspect(Stream xlsxStream, string? sheetName, int headerRowIndex, int dataStartColumnIndex)
    {
        var sheetNames = spreadsheetReader.GetSheetNames(xlsxStream);
        var resolvedSheet = sheetName is not null && sheetNames.Contains(sheetName, StringComparer.OrdinalIgnoreCase)
            ? sheetNames.First(s => string.Equals(s, sheetName, StringComparison.OrdinalIgnoreCase))
            : sheetNames.First();
        var headers = spreadsheetReader.ReadHeaderRow(xlsxStream, resolvedSheet, headerRowIndex, dataStartColumnIndex);
        return new GuestImportInspectResult(sheetNames, headers);
    }

    public Task<GuestImportResult> PreviewAsync(
        TenantContext tenant, Stream xlsxStream, GuestImportColumnMapping mapping, CancellationToken ct) =>
        ProcessAsync(tenant, xlsxStream, mapping, commit: false, actor: "preview", ct);

    public Task<GuestImportResult> CommitAsync(
        TenantContext tenant, Stream xlsxStream, GuestImportColumnMapping mapping, string actor, CancellationToken ct) =>
        ProcessAsync(tenant, xlsxStream, mapping, commit: true, actor, ct);

    private async Task<GuestImportResult> ProcessAsync(
        TenantContext tenant, Stream xlsxStream, GuestImportColumnMapping mapping, bool commit, string actor, CancellationToken ct)
    {
        var dataRows = spreadsheetReader.ReadDataRows(
            xlsxStream, mapping.SheetName, mapping.HeaderRowIndex, mapping.DataStartColumnIndex);

        var rows = new List<GuestImportRowResult>();
        var newGuestCount = 0;
        var updatedGuestCount = 0;
        var assignmentCount = 0;
        var warningCount = 0;

        var workloads = await workloadRepository.ListAsync(tenant, ct);

        var rowNumber = mapping.HeaderRowIndex + 1;
        foreach (var rawRow in dataRows)
        {
            var rowValues = new Dictionary<string, string>();
            foreach (var (columnOffset, field) in mapping.ColumnToField)
            {
                if (rawRow.TryGetValue(columnOffset, out var value) && !string.IsNullOrEmpty(value))
                {
                    rowValues[field] = value.Trim();
                }
            }

            var result = await ProcessRowAsync(tenant, rowNumber, rowValues, workloads, commit, actor, ct);
            rows.Add(result);
            if (result.IsNewGuest) newGuestCount++;
            else if (result.DataChanged) updatedGuestCount++;
            assignmentCount += result.MatchedRoleNames.Count;
            warningCount += result.Warnings.Count;

            rowNumber++;
        }

        return new GuestImportResult(rows, newGuestCount, updatedGuestCount, assignmentCount, warningCount);
    }

    private async Task<GuestImportRowResult> ProcessRowAsync(
        TenantContext tenant, int rowNumber, Dictionary<string, string> rowValues,
        IReadOnlyList<Workload> workloads, bool commit, string actor, CancellationToken ct)
    {
        var warnings = new List<GuestImportRowWarning>();

        if (!rowValues.TryGetValue(GuestImportReservedFields.Mail, out var mail) || string.IsNullOrWhiteSpace(mail))
        {
            return new GuestImportRowResult(rowNumber, string.Empty, string.Empty, false, false, [],
                [new GuestImportRowWarning("Keine Mail-Adresse in dieser Zeile — Zeile übersprungen.")], []);
        }

        var displayName = rowValues.GetValueOrDefault(GuestImportReservedFields.DisplayName, mail);
        var workloadName = rowValues.GetValueOrDefault(GuestImportReservedFields.Workload);
        var scenarioName = rowValues.GetValueOrDefault(GuestImportReservedFields.Szenario);

        var fields = rowValues
            .Where(kv => !GuestImportReservedFields.All.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        Workload? workload = null;
        WorkloadScenario? scenario = null;
        var matchedRoleNames = new List<string>();

        if (string.IsNullOrWhiteSpace(workloadName))
        {
            warnings.Add(new GuestImportRowWarning("Keine Workload-Spalte gemappt oder Wert leer — keine Zuweisung erzeugt."));
        }
        else
        {
            workload = workloads.FirstOrDefault(w => string.Equals(w.Name, workloadName, StringComparison.OrdinalIgnoreCase));
            if (workload is null)
            {
                warnings.Add(new GuestImportRowWarning($"Workload '{workloadName}' nicht gefunden."));
            }
        }

        if (workload is not null)
        {
            if (string.IsNullOrWhiteSpace(scenarioName))
            {
                warnings.Add(new GuestImportRowWarning("Keine Szenario-Spalte gemappt oder Wert leer — keine Zuweisung erzeugt."));
            }
            else
            {
                var scenarios = await scenarioRepository.ListByWorkloadAsync(tenant, workload.Id, ct);
                scenario = scenarios.FirstOrDefault(s => string.Equals(s.Name, scenarioName, StringComparison.OrdinalIgnoreCase));
                if (scenario is null)
                {
                    warnings.Add(new GuestImportRowWarning($"Szenario '{scenarioName}' nicht im Workload '{workloadName}' gefunden."));
                }
            }
        }

        var matchedRoleIds = new HashSet<Guid>();
        if (workload is not null && scenario is not null)
        {
            var matchedAnyRule = false;
            foreach (var rule in scenario.Rules)
            {
                if (!RuleMatchesFields(rule, fields))
                {
                    continue;
                }
                matchedAnyRule = true;

                var roles = workload.Roles.Where(r => r.ResourceMappings.Contains(rule.ResourceId)).ToList();
                if (roles.Count == 0)
                {
                    warnings.Add(new GuestImportRowWarning(
                        $"Regel {rule.Id} passt, aber keine WorkloadRole verweist auf die zugehörige Ressource."));
                    continue;
                }
                foreach (var role in roles)
                {
                    if (matchedRoleIds.Add(role.Id))
                    {
                        matchedRoleNames.Add(role.Name);
                    }
                }
            }

            if (!matchedAnyRule)
            {
                warnings.Add(new GuestImportRowWarning(
                    $"Keine Regel in Szenario '{scenarioName}' passt zu den Feldwerten dieser Zeile."));
            }
        }

        var existingGuest = await guestRepository.GetByMailAsync(tenant, mail, ct);
        var isNewGuest = existingGuest is null;
        var dataChanged = existingGuest is not null && !string.Equals(existingGuest.DisplayName, displayName, StringComparison.Ordinal);

        var foreignImpacts = new List<GuestImportForeignWorkloadImpact>();
        if (existingGuest is not null && dataChanged && workload is not null)
        {
            var existingAssignments = await assignmentRepository.ListActiveByGuestAsync(tenant, existingGuest.Id, ct);
            foreach (var assignment in existingAssignments.Where(a => a.WorkloadId != workload.Id))
            {
                var otherWorkload = workloads.FirstOrDefault(w => w.Id == assignment.WorkloadId);
                var reason = $"Gast-Daten haben sich geändert (DisplayName: '{existingGuest.DisplayName}' -> " +
                    $"'{displayName}') — bitte prüfen, ob die Zuweisung in Workload " +
                    $"'{otherWorkload?.Name ?? assignment.WorkloadId.ToString()}' noch gültig ist.";
                foreignImpacts.Add(new GuestImportForeignWorkloadImpact(
                    assignment.WorkloadId, otherWorkload?.Name ?? assignment.WorkloadId.ToString(), assignment.Id, reason));
            }
        }

        if (commit)
        {
            var guest = existingGuest ?? new GuestAccount
            {
                PlatformTenantId = tenant.PlatformTenantId,
                DirectoryTenantId = tenant.DirectoryTenantId ?? string.Empty,
                Mail = mail,
                DisplayName = displayName,
            };
            if (existingGuest is not null)
            {
                guest.DisplayName = displayName;
            }
            else
            {
                guest.TransitionTo(GuestAccountState.Discovered);
            }
            guest.UpdatedAt = DateTimeOffset.UtcNow;
            await guestRepository.UpsertAsync(guest, ct);

            var correlationId = Guid.NewGuid();
            foreach (var roleId in matchedRoleIds)
            {
                var request = new GrantWorkloadRoleRequest(
                    tenant.PlatformTenantId, guest.Id, workload!.Id, roleId, Actor: actor);
                await grantWorkloadRoleHandler.HandleAsync(request, ct);
            }

            await auditService.RecordAsync(
                tenant.PlatformTenantId, actor, "ImportGuestRow", nameof(GuestAccount),
                guest.Id.ToString(), isNewGuest ? "Created" : "Updated", correlationId,
                details: $"Zeile {rowNumber}, {matchedRoleIds.Count} Zuweisung(en), " +
                    $"{foreignImpacts.Count} Fremd-Workload-Review(s).", ct: ct);

            if (foreignImpacts.Count > 0)
            {
                await CreateReviewItemsAsync(tenant, foreignImpacts, ct);
            }
        }

        return new GuestImportRowResult(
            rowNumber, mail, displayName, isNewGuest, dataChanged, matchedRoleNames, warnings, foreignImpacts);
    }

    private static bool RuleMatchesFields(ScenarioResourceRule rule, Dictionary<string, string> fields)
    {
        foreach (var (key, expected) in rule.Fields)
        {
            if (!fields.TryGetValue(key, out var actual))
            {
                continue; // Regel-Schlüssel ohne Entsprechung im Mapping wird ignoriert.
            }
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.Condition is System.Text.Json.JsonElement condition)
        {
            var context = new ScenarioEvaluationContext(
                GuestAccountState: string.Empty,
                ActiveAssignmentCount: 0,
                Fields: fields,
                AdditionalFacts: new Dictionary<string, System.Text.Json.JsonElement>());
            return JsonLogicEvaluator.Evaluate(condition, context);
        }

        return true;
    }

    private async Task CreateReviewItemsAsync(
        TenantContext tenant, List<GuestImportForeignWorkloadImpact> impacts, CancellationToken ct)
    {
        var openReviews = await reviewRepository.ListOpenAsync(tenant, ct);
        var instance = openReviews.FirstOrDefault(r => r.ReviewDefinitionId == GuestImportReviewDefinitionId)
            ?? new ReviewInstance
            {
                PlatformTenantId = tenant.PlatformTenantId,
                ReviewDefinitionId = GuestImportReviewDefinitionId,
                Provider = GovernanceProvider.Internal,
            };

        foreach (var impact in impacts)
        {
            instance.Items.Add(new ReviewItem
            {
                ReviewInstanceId = instance.Id,
                AssignmentId = impact.AssignmentId,
                Reason = impact.Reason,
            });
        }

        await reviewRepository.UpsertAsync(instance, ct);
    }
}
