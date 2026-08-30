using System.Security.Claims;
using B2B.Portal.Infrastructure.Auth;

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

/// <summary>
/// Liest den PortalUserContext aus dem validierten JWT (HttpContext.User), das die
/// JwtBearer-Middleware in Program.cs bereits geprueft hat — kein erneutes Parsen von
/// Headern. Ersetzt die fruehere HeaderPortalUserContextAccessor (freie X-Portal-*-Header),
/// das Interface bleibt unveraendert, damit der komplette Endpoint-Code in Program.cs
/// unangetastet bleibt.
/// </summary>
public sealed class ClaimsPortalUserContextAccessor(IHttpContextAccessor httpContextAccessor) : IPortalUserContextAccessor
{
    public PortalUserContext Current
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Kein HttpContext verfügbar.");
            var user = ctx.User;

            if (user.Identity is not { IsAuthenticated: true })
            {
                throw new UnauthorizedAccessException(
                    "Kein gueltiges Bearer-Token. Login ueber POST /api/auth/mock/login " +
                    "(EntraIdMock) bzw. den konfigurierten Identity Provider.");
            }

            var mail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(mail))
            {
                throw new UnauthorizedAccessException("Token enthaelt keinen email-Claim.");
            }

            var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (roles.Count == 0)
            {
                roles.Add(PortalRoles.User);
            }

            var scenarioManagerWorkloadIds = user.FindAll(PortalJwtClaimTypes.ScenarioManagerWorkloadId)
                .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            return new PortalUserContext(mail, roles, scenarioManagerWorkloadIds);
        }
    }
}

