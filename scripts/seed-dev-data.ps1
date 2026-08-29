# Seed-Skript für lokale Entwicklungsdaten (LOCAL_MOCK).
# Nutzt ausschliesslich die Mock-Endpunkte der Portal API - keine echten Graph-Schreibzugriffe.
param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$PlatformTenantId = "dev-tenant-a"
)

Write-Host "Seede Entwicklungsdaten gegen $ApiBaseUrl fuer Tenant $PlatformTenantId (LOCAL_MOCK)..."

$headers = @{ "X-Platform-Tenant-Id" = $PlatformTenantId; "Content-Type" = "application/json" }

$body = @{ mail = "anna@contoso.example"; displayName = "Anna Contoso" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/guests/invite" -Headers $headers -Body $body

Write-Host "Fertig."
