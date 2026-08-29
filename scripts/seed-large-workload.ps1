<#
.SYNOPSIS
    Erzeugt aussagekraeftige Mockdaten: einen Workload mit mehreren Rollen und 500
    (konfigurierbar) Gaeste inkl. Assignments, ueber die LOCAL_MOCK Portal API.

.DESCRIPTION
    Ruft den Dev-Only-Seed-Endpoint POST /api/dev/seed/large-workload auf
    (siehe src/B2B.Portal.Api/Program.cs, nur unter B2B_MODE=LOCAL_MOCK registriert).
    Legt direkt ueber die InMemory-Repositories an, statt 500 einzelne HTTP-Requests zu
    schicken - daher sehr schnell (typ. < 1s), aber ueber denselben Code-Pfad wie die
    echten Commands (ProvisioningService, AuditService).

    Erzeugte Daten:
      - 1 Workload ("SAP S/4 Rollout - Projekt Meridian" oder eigener Name) mit 5
        Ressourcen (SecurityGroups, M365Group, Team, AppRole) und 4 Rollen
        (Reader/Contributor/Core Team/Project Admin).
      - N Gaeste mit realistischen, aber offensichtlich fiktiven Namen/Firmen/Mailadressen
        (.example-Domains nach RFC 2606), verteilt ueber 8 Beispielorganisationen.
      - Rollenverteilung 65% Reader / 20% Contributor / 10% Core Team / 5% Admin.
      - Lifecycle-Status-Mix: ueberwiegend Active, einige Invited/OrphanCandidate/
        Discovered, damit Filter/Badges in der UI unterschiedliche Zustaende zeigen.
      - Assignment-Status-Mix: ueberwiegend Active, einige PendingReview/Requested/Expired.

    Voraussetzung: Portal API laeuft lokal im LOCAL_MOCK-Modus (siehe README Quick Start /
    .vscode/launch.json "Portal API").

.PARAMETER ApiBaseUrl
    Basis-URL der laufenden Portal API. Default http://localhost:5000 (bzw. aus
    .env.local, falls vorhanden).

.PARAMETER PlatformTenantId
    Platform-Tenant, unter dem die Daten angelegt werden. Default "dev-tenant-a".

.PARAMETER GuestCount
    Anzahl zu erzeugender Gaeste. Default 500, maximal 5000 (serverseitig begrenzt).

.PARAMETER WorkloadName
    Optionaler eigener Workload-Name statt des Default-Demo-Namens.

.EXAMPLE
    ./scripts/seed-large-workload.ps1
    # 500 Gaeste, Default-Workload, gegen http://localhost:5000, Tenant dev-tenant-a.

.EXAMPLE
    ./scripts/seed-large-workload.ps1 -GuestCount 1500 -WorkloadName "Onboarding-Projekt Nord"
#>
param(
    [string]$ApiBaseUrl,
    [string]$PlatformTenantId = "dev-tenant-a",
    [int]$GuestCount = 500,
    [string]$WorkloadName
)

$ErrorActionPreference = "Stop"

if (-not $ApiBaseUrl) {
    $envLocalPath = Join-Path (Split-Path $PSScriptRoot -Parent) ".env.local"
    $ApiBaseUrl = "http://localhost:5000"
    if (Test-Path $envLocalPath) {
        $match = Get-Content $envLocalPath | Where-Object { $_ -match "^API_BASE_URL=(.+)$" }
        if ($match) { $ApiBaseUrl = $Matches[1] }
    }
}

Write-Host "Seede $GuestCount Gaeste + 1 Workload gegen $ApiBaseUrl (Tenant $PlatformTenantId)..." -ForegroundColor Cyan

try {
    $health = Invoke-RestMethod -Uri "$ApiBaseUrl/health" -ErrorAction Stop
    if ($health.mode -ne "LOCAL_MOCK") {
        throw "API laeuft im Modus '$($health.mode)', nicht LOCAL_MOCK - der Seed-Endpoint ist nur unter LOCAL_MOCK registriert."
    }
}
catch {
    throw "Portal API unter $ApiBaseUrl nicht erreichbar oder nicht im LOCAL_MOCK-Modus. Starte sie zuerst (siehe README Quick Start). Fehler: $($_.Exception.Message)"
}

$body = @{ guestCount = $GuestCount }
if ($WorkloadName) { $body.workloadName = $WorkloadName }

$headers = @{ "X-Platform-Tenant-Id" = $PlatformTenantId; "Content-Type" = "application/json" }
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$result = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/dev/seed/large-workload" `
    -Headers $headers -Body ($body | ConvertTo-Json) -ErrorAction Stop
$stopwatch.Stop()

Write-Host ""
Write-Host "Fertig in $($stopwatch.ElapsedMilliseconds) ms." -ForegroundColor Green
Write-Host "  Workload: '$($result.workloadName)' ($($result.workloadId))"
Write-Host "  Rollen:   $($result.roles.name -join ', ')"
Write-Host "  Gaeste:   $($result.guestCount)"
Write-Host ""
Write-Host "Ansehen: Web-UI unter Guest Pool / Workloads (Admin-Ansicht), oder direkt:" -ForegroundColor Cyan
Write-Host "  curl -H 'X-Platform-Tenant-Id: $PlatformTenantId' $ApiBaseUrl/api/guest-accounts"
Write-Host "  curl -H 'X-Platform-Tenant-Id: $PlatformTenantId' $ApiBaseUrl/api/workloads"
