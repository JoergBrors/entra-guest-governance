using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace B2B.Portal.Infrastructure.Auth;

/// <summary>
/// Claim-Namen fuer die von EntraIdMock ausgestellten Tokens. Zentral definiert, damit
/// Ausstellung (hier) und Validierung/Lesen (ClaimsPortalUserContextAccessor,
/// ClaimsTenantContextAccessor in B2B.Portal.Api) denselben Namen verwenden.
/// </summary>
public static class PortalJwtClaimTypes
{
    public const string PlatformTenantId = "platformTenantId";
    public const string ScenarioManagerWorkloadId = "scenarioManagerWorkloadId";
}

/// <summary>
/// Stellt JWTs fuer den EntraIdMock-Identity-Provider aus (kein Passwort, nur Auswahl einer
/// bekannten Mock-Entra-Mail — siehe /api/auth/mock/login in Program.cs). Signiert
/// symmetrisch mit dem in IdentityProviderConfig konfigurierten Key.
/// </summary>
public sealed class MockJwtIssuer(IdentityProviderConfig config)
{
    public string IssueToken(
        string objectId,
        string mail,
        IReadOnlyCollection<string> roles,
        string platformTenantId,
        IReadOnlyCollection<Guid> scenarioManagerWorkloadIds)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, objectId),
            new(JwtRegisteredClaimNames.Email, mail),
            new(PortalJwtClaimTypes.PlatformTenantId, platformTenantId),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(scenarioManagerWorkloadIds.Select(
            id => new Claim(PortalJwtClaimTypes.ScenarioManagerWorkloadId, id.ToString())));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: IdentityProviderConfig.JwtIssuer,
            audience: IdentityProviderConfig.JwtAudience,
            claims: claims,
            notBefore: now,
            expires: now.Add(config.TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
