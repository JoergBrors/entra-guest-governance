namespace B2B.Portal.Api.Auth;

public static class PortalRoles
{
    public const string User = nameof(User);
    public const string Reviewer = nameof(Reviewer);
    public const string WorkloadOwner = nameof(WorkloadOwner);
    public const string ScenarioManager = nameof(ScenarioManager);
    public const string GovernanceAdmin = nameof(GovernanceAdmin);
}

public sealed record PortalUserContext(
    string Mail,
    IReadOnlySet<string> Roles,
    IReadOnlySet<Guid> ScenarioManagerWorkloadIds)
{
    public bool HasRole(string role) => Roles.Contains(role);

    public bool IsGovernanceAdmin => HasRole(PortalRoles.GovernanceAdmin);

    public bool CanReview => IsGovernanceAdmin || HasRole(PortalRoles.Reviewer);

    public bool CanManageWorkload(string? workloadOwner) =>
        IsGovernanceAdmin ||
        (HasRole(PortalRoles.WorkloadOwner) &&
            !string.IsNullOrWhiteSpace(workloadOwner) &&
            string.Equals(Mail, workloadOwner, StringComparison.OrdinalIgnoreCase));

    public bool CanManageScenario(Guid workloadId, string? workloadOwner, IReadOnlyCollection<string> scenarioManagers) =>
        CanManageWorkload(workloadOwner) ||
        (HasRole(PortalRoles.ScenarioManager) &&
            (ScenarioManagerWorkloadIds.Contains(workloadId) ||
             scenarioManagers.Any(m => string.Equals(m, Mail, StringComparison.OrdinalIgnoreCase))));
}

public interface IPortalUserContextAccessor
{
    PortalUserContext Current { get; }
}

public sealed class HeaderPortalUserContextAccessor(IHttpContextAccessor httpContextAccessor) : IPortalUserContextAccessor
{
    public PortalUserContext Current
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Kein HttpContext verfügbar.");

            var mail = ctx.Request.Headers["X-Portal-User-Mail"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(mail))
            {
                throw new UnauthorizedAccessException(
                    "X-Portal-User-Mail fehlt. In DEV_INTEGRATION/AZURE_DEV wird dieser Wert aus dem validierten Entra-Token abgeleitet.");
            }

            var roles = ParseSet(ctx.Request.Headers["X-Portal-Roles"].FirstOrDefault());
            if (roles.Count == 0)
            {
                roles.Add(PortalRoles.User);
            }

            var scenarioManagerWorkloadIds = ParseSet(ctx.Request.Headers["X-Scenario-Manager-Workload-Ids"].FirstOrDefault())
                .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            return new PortalUserContext(mail, roles, scenarioManagerWorkloadIds);
        }
    }

    private static HashSet<string> ParseSet(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

