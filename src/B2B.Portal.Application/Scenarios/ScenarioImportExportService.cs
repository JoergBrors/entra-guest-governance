using System.Text.Json;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Services;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Application.Scenarios;

/// <summary>
/// Importiert ein vollständiges Szenario-Template: löst den Ziel-Workload per Name auf,
/// legt referenzierte Ressourcen (Name+Art) automatisch an, falls sie noch nicht
/// existieren, validiert alle Bedingungen vorab, und legt/aktualisiert ein WorkloadScenario
/// mit den resultierenden ScenarioResourceRules. Lebt in Application (nicht Domain), weil
/// die Namensauflösung/-anlage Application-Ports (IWorkloadRepository,
/// IWorkloadScenarioRepository) braucht — eine Domain-Platzierung wäre architektonisch
/// rückwärts (Domain kennt keine Ports). Export macht die Rückwärts-Auflösung für den
/// JSON-Download desselben Template-Formats.
/// </summary>
public sealed class ScenarioImportExportService(
    IWorkloadRepository workloadRepository,
    IWorkloadScenarioRepository scenarioRepository)
{
    public async Task<ScenarioImportResult> ImportAsync(
        TenantContext tenant, ScenarioTemplateDto template, CancellationToken ct)
    {
        var errors = new List<string>();
        var createdResourceNames = new List<string>();

        var workloads = await workloadRepository.ListAsync(tenant, ct);
        var workload = workloads.FirstOrDefault(
            w => string.Equals(w.Name, template.WorkloadName, StringComparison.OrdinalIgnoreCase));
        if (workload is null)
        {
            errors.Add($"Workload '{template.WorkloadName}' nicht gefunden.");
            return new ScenarioImportResult(null, createdResourceNames, errors);
        }

        var existingScenarios = await scenarioRepository.ListByWorkloadAsync(tenant, workload.Id, ct);
        var scenario = existingScenarios.FirstOrDefault(
            s => string.Equals(s.Name, template.ScenarioName, StringComparison.OrdinalIgnoreCase))
            ?? new WorkloadScenario
            {
                PlatformTenantId = tenant.PlatformTenantId,
                WorkloadId = workload.Id,
                Name = template.ScenarioName,
            };

        var workloadChanged = false;
        var rules = new List<ScenarioResourceRule>();

        foreach (var ruleDto in template.Rules)
        {
            var resource = workload.Resources.FirstOrDefault(r =>
                string.Equals(r.ResourceType, ruleDto.ResourceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ExternalId, ruleDto.ResourceName, StringComparison.OrdinalIgnoreCase));

            if (resource is null)
            {
                resource = new WorkloadResource
                {
                    WorkloadId = workload.Id,
                    ResourceType = ruleDto.ResourceType,
                    ExternalId = ruleDto.ResourceName,
                    Managed = true,
                };
                workload.Resources.Add(resource);
                workloadChanged = true;
                createdResourceNames.Add($"{ruleDto.ResourceType}:{ruleDto.ResourceName}");
            }

            if (ruleDto.Condition is JsonElement condition)
            {
                try
                {
                    JsonLogicEvaluator.Validate(condition);
                }
                catch (NotSupportedException ex)
                {
                    errors.Add($"Regel für Ressource '{ruleDto.ResourceName}': {ex.Message}");
                    continue;
                }
            }

            rules.Add(new ScenarioResourceRule
            {
                WorkloadScenarioId = scenario.Id,
                ResourceId = resource.Id,
                Fields = new Dictionary<string, string>(ruleDto.Fields),
                Condition = ruleDto.Condition,
            });
        }

        if (workloadChanged)
        {
            workload.UpdatedAt = DateTimeOffset.UtcNow;
            await workloadRepository.UpsertAsync(workload, ct);
        }

        scenario.Rules.Clear();
        scenario.Rules.AddRange(rules);
        scenario.UpdatedAt = DateTimeOffset.UtcNow;
        await scenarioRepository.UpsertAsync(scenario, ct);

        return new ScenarioImportResult(scenario.Id, createdResourceNames, errors);
    }

    public async Task<ScenarioTemplateDto> ExportAsync(TenantContext tenant, Guid scenarioId, CancellationToken ct)
    {
        var scenario = await scenarioRepository.GetAsync(tenant, scenarioId, ct)
            ?? throw new InvalidOperationException($"WorkloadScenario {scenarioId} nicht gefunden.");
        var workload = await workloadRepository.GetAsync(tenant, scenario.WorkloadId, ct)
            ?? throw new InvalidOperationException($"Workload {scenario.WorkloadId} nicht gefunden.");

        var resourcesById = workload.Resources.ToDictionary(r => r.Id);

        var ruleDtos = new List<ScenarioTemplateRuleDto>();
        foreach (var rule in scenario.Rules)
        {
            if (!resourcesById.TryGetValue(rule.ResourceId, out var resource))
            {
                continue; // Verwaiste Regel (Ressource wurde inzwischen entfernt) — beim Export übersprungen.
            }

            ruleDtos.Add(new ScenarioTemplateRuleDto(
                resource.ExternalId ?? resource.Id.ToString(),
                resource.ResourceType,
                rule.Fields,
                rule.Condition));
        }

        return new ScenarioTemplateDto(workload.Name, scenario.Name, ruleDtos);
    }
}
