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

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var scenarioId = Guid.Parse(job.EntityId);
        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);

        var scenario = await scenarioRepository.GetAsync(tenant, scenarioId, ct);
        if (scenario is null)
        {
            logger.LogWarning("DeployScenario: WorkloadScenario {ScenarioId} nicht gefunden.", scenarioId);
            return;
        }

        var workload = await workloadRepository.GetAsync(tenant, scenario.WorkloadId, ct);
        if (workload is null)
        {
            logger.LogWarning(
                "DeployScenario: Workload {WorkloadId} für Szenario {ScenarioId} nicht gefunden.",
                scenario.WorkloadId, scenarioId);
            return;
        }

        var resourcesById = workload.Resources.ToDictionary(r => r.Id);
        var deployedCount = 0;
        var skippedCount = 0;

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
            // beschriebene Ressource selbst bereit.
            var metadata = new Dictionary<string, string>(rule.Fields)
            {
                ["ScenarioId"] = scenario.Id.ToString(),
                ["ResourceType"] = resource.ResourceType,
            };
            await connector.CreateResourceAsync(
                directoryTenantId: job.DirectoryTenantId ?? string.Empty,
                namePattern: $"{workload.Name}-{scenario.Name}-{resource.ResourceType}-{resource.ExternalId}",
                metadata: metadata,
                ct);
            deployedCount++;
        }

        logger.LogInformation(
            "DeployScenario {ScenarioId} ({Name}): {Deployed} Ressourcen deployt, {Skipped} durch Bedingung " +
            "übersprungen. CorrelationId={CorrelationId}",
            scenarioId, scenario.Name, deployedCount, skippedCount, job.CorrelationId);
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
