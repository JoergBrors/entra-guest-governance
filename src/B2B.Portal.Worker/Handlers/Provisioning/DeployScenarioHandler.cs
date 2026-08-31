using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Services;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Provisioning;

/// <summary>
/// Deployt ein WorkloadScenario: iteriert seine ScenarioResourceRules, wertet je Regel die
/// optionale Bedingung per JsonLogicEvaluator gegen die Fields der Regel aus, und stellt
/// nur bei erfüllter (oder fehlender) Bedingung die zugehörige WorkloadResource über
/// IResourceConnector.CreateResourceAsync sicher. Gehört zur bestehenden Provisioning-
/// Handlergruppe (ADR-0001: jeder neue JobType muss in das bestehende
/// IJobHandler/JobDispatcher-Muster passen, keine eigene Ausführungsschiene). Fakten
/// werden vorher aus dem Job-/Regel-Kontext gesammelt (gather-then-evaluate, dasselbe
/// Muster wie LifecycleService.EvaluateDeletionAsync + DeletionGateEvaluator).
/// Ruft NIEMALS GuestAccount.TransitionTo auf — nur Workload-Ressourcen werden angefasst
/// (Governance-Core-Invariante, Anhang A Regel 3).
/// </summary>
public sealed class DeployScenarioHandler(
    IWorkloadScenarioRepository scenarioRepository,
    IWorkloadRepository workloadRepository,
    IResourceConnector connector,
    ILogger<DeployScenarioHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.DeployScenario;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var scenarioId = Guid.Parse(job.EntityId);
        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);

        var scenario = await scenarioRepository.GetAsync(tenant, scenarioId, ct);
        if (scenario is null)
        {
            logger.LogWarning("DeployScenario: WorkloadScenario {ScenarioId} nicht gefunden.", scenarioId);
            return $"WorkloadScenario {scenarioId} nicht gefunden — nichts deployt.";
        }

        var workload = await workloadRepository.GetAsync(tenant, scenario.WorkloadId, ct);
        if (workload is null)
        {
            logger.LogWarning(
                "DeployScenario: Workload {WorkloadId} für Szenario {ScenarioId} nicht gefunden.",
                scenario.WorkloadId, scenarioId);
            return $"Workload {scenario.WorkloadId} fuer Szenario '{scenario.Name}' nicht gefunden — nichts deployt.";
        }

        var resourcesById = workload.Resources.ToDictionary(r => r.Id);
        var deployedCount = 0;
        var skippedCount = 0;
        var workloadChanged = false;
        var deployedResourceNames = new List<string>();

        foreach (var rule in scenario.Rules)
        {
            if (!resourcesById.TryGetValue(rule.ResourceId, out var resource))
            {
                logger.LogWarning(
                    "DeployScenario {ScenarioId}: WorkloadResource {ResourceId} nicht im Workload {WorkloadId} gefunden.",
                    scenarioId, rule.ResourceId, workload.Id);
                continue;
            }

            if (rule.Condition is System.Text.Json.JsonElement condition)
            {
                var context = BuildEvaluationContext(rule);
                if (!JsonLogicEvaluator.Evaluate(condition, context))
                {
                    skippedCount++;
                    continue;
                }
            }

            // CreateResourceAsync ist idempotent-freundlich gestaltet (Connector-Vertrag)
            // und stellt sicher, dass die Ziel-Ressource (Gruppe/Team) existiert — ein
            // Szenario-Deployment grant-et keinem einzelnen Gast Zugriff (das bleibt
            // GrantWorkloadRoleHandler vorbehalten), sondern stellt die pro Regel
            // beschriebene Ressource selbst bereit. namePattern nutzt bewusst noch den
            // (evtl. veralteten) DisplayName-Snapshot bzw. die alte ExternalId nur als
            // Fallback fuer die Namensbildung beim Connector — massgeblich fuer die
            // Ressourcen-IDENTITAET ist ausschliesslich die unten zurueckgeschriebene ObjectId.
            var namePatternSuffix = resource.DisplayName ?? resource.ExternalId ?? resource.Id.ToString();
            var metadata = new Dictionary<string, string>(rule.Fields)
            {
                ["ScenarioId"] = scenario.Id.ToString(),
                ["ResourceType"] = resource.ResourceType,
            };
            var namePattern = $"{workload.Name}-{scenario.Name}-{resource.ResourceType}-{namePatternSuffix}";
            var objectId = await connector.CreateResourceAsync(
                directoryTenantId: job.DirectoryTenantId ?? string.Empty,
                namePattern: namePattern,
                metadata: metadata,
                ct);

            // Root-Cause-Fix (Erweiterung 2026-08-31): vorher wurde die von CreateResourceAsync
            // zurueckgegebene ObjectId verworfen — resource.ExternalId blieb dauerhaft leer/
            // veraltet, obwohl der Connector die Ressource bereits angelegt hatte. Jetzt wird
            // die ObjectId als stabile Referenz persistiert, namePattern als DisplayName-
            // Snapshot uebernommen (siehe WorkloadResource-Kommentar: ExternalId = ObjectId,
            // DisplayName = informativer Snapshot).
            if (!string.Equals(resource.ExternalId, objectId, StringComparison.OrdinalIgnoreCase))
            {
                resource.ExternalId = objectId;
                resource.DisplayName = namePattern;
                workloadChanged = true;
            }
            deployedCount++;
            deployedResourceNames.Add(namePattern);
        }

        if (workloadChanged)
        {
            workload.UpdatedAt = DateTimeOffset.UtcNow;
            await workloadRepository.UpsertAsync(workload, ct);
        }

        logger.LogInformation(
            "DeployScenario {ScenarioId} ({Name}): {Deployed} Ressourcen deployt, {Skipped} durch Bedingung " +
            "übersprungen. CorrelationId={CorrelationId}",
            scenarioId, scenario.Name, deployedCount, skippedCount, job.CorrelationId);

        return $"Szenario '{scenario.Name}' auf Workload '{workload.Name}': {deployedCount} Ressource(n) deployt " +
            $"[{string.Join(", ", deployedResourceNames)}], {skippedCount} durch Bedingung uebersprungen.";
    }

    // Fakten-Sammlung für die JSONLogic-Auswertung einer einzelnen Regel ("gather facts,
    // then evaluate", dasselbe Muster wie LifecycleService vor DeletionGateEvaluator). Die
    // Fields der Regel selbst (Firma, Rolle, Environment, ...) sind die primäre
    // Fakten-Quelle. GuestAccountState/ActiveAssignmentCount bleiben im MVP neutral, da ein
    // Szenario-Deployment nicht an einen einzelnen Gast gebunden ist — nicht erfunden,
    // sondern bewusst leer belassen (dokumentierte MVP-Lücke).
    private static ScenarioEvaluationContext BuildEvaluationContext(ScenarioResourceRule rule) => new(
        GuestAccountState: string.Empty,
        ActiveAssignmentCount: 0,
        Fields: rule.Fields,
        AdditionalFacts: new Dictionary<string, System.Text.Json.JsonElement>());
}
