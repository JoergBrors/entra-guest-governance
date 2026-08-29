# Minimaler Smoke-Test gegen eine laufende LOCAL_MOCK-Instanz.
param([string]$ApiBaseUrl = "http://localhost:5000")

$health = Invoke-RestMethod -Uri "$ApiBaseUrl/health"
if ($health.status -ne "healthy") {
    throw "API meldet keinen 'healthy' Status: $($health | ConvertTo-Json)"
}
Write-Host "Health OK: $($health | ConvertTo-Json -Compress)"
