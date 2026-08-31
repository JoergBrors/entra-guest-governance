namespace B2B.Portal.Infrastructure.Auth;

/// <summary>
/// Backend-konfigurierte Identity-Provider-Auswahl (nicht client-konfigurierbar — im
/// Gegensatz zu den frueheren freien X-Portal-*-Headern). Folgt demselben Konfigurations-
/// Muster wie B2B_MODE/DIRECTORY_PROVIDER/EMAIL_PROVIDER in
/// InfrastructureServiceCollectionExtensions.
/// </summary>
public enum IdentityProviderKind
{
    /// <summary>Login gegen den lokalen Mock-Entra-Stamm (MockEntraDirectoryStore),
    /// kein Passwort, nur Auswahl einer bekannten Mail. Nur unter LOCAL_MOCK sinnvoll.</summary>
    EntraIdMock,

    /// <summary>Platzhalter fuer echtes OIDC gegen einen Entra-Tenant. Noch nicht
    /// implementiert — `integration pending` (siehe docs/architecture/graph-integration.md
    /// fuer das etablierte Muster fuer bewusst unimplementierte Integrationspunkte).</summary>
    EntraId,
}

/// <summary>
/// Konfigurationsschema fuer den aktiven Identity Provider. Wird aus IDENTITY_PROVIDER
/// (env var / .env.local) gelesen, analog zu B2B_MODE. Default unter LOCAL_MOCK ist
/// EntraIdMock.
/// </summary>
public sealed record IdentityProviderConfig(
    IdentityProviderKind Kind,
    // EntraIdMock: symmetrischer Signing-Key fuer die lokal ausgestellten JWTs.
    string JwtSigningKey,
    TimeSpan TokenLifetime,
    // EntraId (Platzhalter): Konfigurationsfelder fuer echtes OIDC. Bleiben bis zur
    // tatsaechlichen Implementierung ungenutzt (integration pending).
    string? EntraAuthority = null,
    string? EntraClientId = null)
{
    public const string JwtIssuer = "b2b-portal-api";
    public const string JwtAudience = "b2b-portal-web";

    public static IdentityProviderConfig FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration, string mode)
    {
        var providerName = configuration["IDENTITY_PROVIDER"] ?? "EntraIdMock";
        var kind = providerName.Equals("EntraId", StringComparison.OrdinalIgnoreCase)
            ? IdentityProviderKind.EntraId
            : IdentityProviderKind.EntraIdMock;

        if (kind == IdentityProviderKind.EntraIdMock && mode != "LOCAL_MOCK")
        {
            throw new NotSupportedException(
                "IDENTITY_PROVIDER=EntraIdMock ist nur unter B2B_MODE=LOCAL_MOCK zulaessig " +
                "(kein Passwort-Login, nicht produktionstauglich).");
        }

        var signingKey = configuration["JWT_SIGNING_KEY"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            // Dev-Ephemeral-Key: nur fuer die Laufzeit dieses Prozesses gueltig. Bei jedem
            // Neustart aendert er sich, d.h. alte Tokens werden automatisch ungueltig.
            // NIEMALS als echtes Secret verwenden — siehe Warnung in Program.cs beim Start.
            signingKey = $"dev-ephemeral-{Guid.NewGuid():N}{Guid.NewGuid():N}";
        }

        return new IdentityProviderConfig(
            kind,
            signingKey,
            TimeSpan.FromHours(8),
            configuration["ENTRA_AUTHORITY"],
            configuration["ENTRA_CLIENT_ID"]);
    }
}
