using System.Text.RegularExpressions;
using B2B.Portal.Application.Workloads;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Directory;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Workloads;

public sealed class SyncWorkloadPatternResourcesHandler(
    WorkloadManagementService workloadService,
    MockEntraDirectoryStore mockEntraStore,
    ILogger<SyncWorkloadPatternResourcesHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.SyncWorkloadPatternResources;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var workloadId = Guid.Parse(job.EntityId);
        var actor = job.Payload.TryGetProperty("Actor", out var actorValue)
            ? actorValue.GetString() ?? "worker"
            : "worker";
        var patterns = job.Payload.TryGetProperty("ResourceNamePatterns", out var patternsValue)
            ? patternsValue.EnumerateArray()
                .Select(p => p.GetString())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList()
            : [];

        if (patterns.Count == 0)
        {
            logger.LogInformation("Pattern-Sync fuer Workload {WorkloadId}: keine Pattern hinterlegt.", workloadId);
            return;
        }

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var attached = 0;
        foreach (var group in mockEntraStore.ListGroups().Where(g => MatchesAnyPattern(g.DisplayName, patterns)))
        {
            var resourceType = group.ResourceProvisioningOptions.Contains("Team", StringComparer.OrdinalIgnoreCase)
                ? "Team"
                : group.GroupTypes.Contains("Unified", StringComparer.OrdinalIgnoreCase)
                    ? "M365Group"
                    : "SecurityGroup";

            await workloadService.AttachResourceAsync(tenant, workloadId, resourceType, group.DisplayName, actor, ct);
            attached++;
        }

        logger.LogInformation(
            "Pattern-Sync fuer Workload {WorkloadId} abgeschlossen: {Count} Gruppe(n) abgeglichen. CorrelationId={CorrelationId}",
            workloadId, attached, job.CorrelationId);
    }

    private static bool MatchesAnyPattern(string value, IEnumerable<string> patterns) =>
        patterns.Any(pattern => MatchesPattern(value, pattern));

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(value, pattern["regex:".Length..], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (pattern.Length >= 2 && pattern.StartsWith('/') && pattern.EndsWith('/'))
        {
            return Regex.IsMatch(value, pattern[1..^1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
