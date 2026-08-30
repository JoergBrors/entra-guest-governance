# Seed-Skript für lokale Entwicklungsdaten (LOCAL_MOCK).
# Nutzt ausschliesslich die Mock-Endpunkte der Portal API - keine echten Graph-Schreibzugriffe.
#
# Erweiterung 2026-08-30 (Teil 3): freie X-Platform-Tenant-Id/X-Portal-*-Header wurden durch
# ein JWT Bearer Token ersetzt (siehe src/B2B.Portal.Api/Program.cs POST /api/auth/mock/login,
# Api/Tenancy/ClaimsTenantContextAccessor.cs liest den Tenant nur noch aus dem Token-Claim).
# Dieses Skript loggt sich daher zuerst als admin@platform.example ein und haengt das Token
# als Authorization-Header an alle folgenden Aufrufe.
param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$AdminMail = "admin@platform.example"
)

$ErrorActionPreference = "Stop"

Write-Host "Seede Entwicklungsdaten gegen $ApiBaseUrl (LOCAL_MOCK)..."

try {
    $login = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/auth/mock/login" `
        -ContentType "application/json" -Body (@{ mail = $AdminMail } | ConvertTo-Json) -ErrorAction Stop
}
catch {
    throw "Login als '$AdminMail' fehlgeschlagen: $($_.Exception.Message)`n" +
        "-> '$AdminMail' wurde im Mock-Entra-Store nicht gefunden. Fuehre zuerst " +
        "./scripts/reset-cosmos-dev-data.ps1 aus, starte die Portal API danach (neu) und " +
        "versuche es dann erneut (siehe docs/development/local-mock.md)."
}

Write-Host "Angemeldet als '$($login.mail)' (Tenant '$($login.platformTenantId)', Rollen: $($login.roles -join ', '))."
$headers = @{ "Authorization" = "Bearer $($login.token)"; "Content-Type" = "application/json" }

$body = @{ mail = "anna@contoso.example"; displayName = "Anna Contoso" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/guests/invite" -Headers $headers -Body $body | Out-Null

Write-Host "Fertig."
