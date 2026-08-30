using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Application.Commands;

public sealed record DeployScenarioRequest(string PlatformTenantId, Guid ScenarioId, string Actor);

/// <summary>
/// Löst das Deployment eines WorkloadScenario aus (frei definierte Ressourcen-Regeln,
/// siehe WorkloadScenario/ScenarioResourceRule). Führt — wie GrantWorkloadRoleCommandHandler
/// — keine synchrone Graph-/Connector-Operation aus, sondern enqueued einen
/// JobTypes.DeployScenario-Job; die eigentliche Provisionierung (inkl.
/// JSONLogic-Bedingungsauswertung je Regel) übernimmt DeployScenarioHandler im Worker.
/// Ruft niemals GuestAccount.TransitionTo auf — ein Szenario darf ausschließlich
/// Workload-Ressourcen (Gruppen/Teams) gewähren, nie die Gastidentität selbst verändern
/// (Governance-Core-Invariante, Anhang A Regel 3).
/// </summary>
public sealed class DeployScenarioCommandHandler(
    IWorkloadScenarioRepository scenarioRepository,
    ProvisioningService provisioningService,
    AuditService auditService)
{
    public async Task<WorkloadScenario> HandleAsync(DeployScenarioRequest request, CancellationToken ct)
    {
        var tenant = TenantContext.Create(request.PlatformTenantId);
        var scenario = await scenarioRepository.GetAsync(tenant, request.ScenarioId, ct)
            ?? throw new InvalidOperationException($"WorkloadScenario {request.ScenarioId} nicht gefunden.");

        var correlationId = Guid.NewGuid();
        var hash = DesiredStateHasher.Hash(
            "DeployScenario", scenario.Id.ToString(),
            string.Join(',', scenario.Rules.Select(r => r.Id)));

        await provisioningService.EnqueueJobAsync(
            request.PlatformTenantId, directoryTenantId: null, JobTypes.DeployScenario,
            nameof(WorkloadScenario), scenario.Id.ToString(), hash,
            new { ScenarioId = scenario.Id, scenario.WorkloadId },
            correlationId, ct, triggeredBy: request.Actor, workloadId: scenario.WorkloadId);

        await auditService.RecordAsync(
            request.PlatformTenantId, request.Actor, "DeployScenario", nameof(WorkloadScenario),
            scenario.Id.ToString(), "Accepted", correlationId, ct: ct);

        return scenario;
    }
}
