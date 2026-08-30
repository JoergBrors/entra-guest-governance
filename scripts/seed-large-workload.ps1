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
    NUR NOCH INFORMATIONELL (Erweiterung 2026-08-30 (Teil 3)). Der Tenant wird jetzt aus dem
    PlatformTenantId-Claim des JWT abgeleitet, das POST /api/auth/mock/login fuer -AdminMail
    ausstellt (siehe Api/Tenancy/ClaimsTenantContextAccessor.cs) - ein frei gesetzter Header
    wird serverseitig nicht mehr gelesen. Bleibt als Parameter erhalten, um bestehende
    Aufrufe nicht zu brechen; steuert nur noch die Ausgabe der Beispiel-curl-Kommandos.

.PARAMETER AdminMail
    Mock-Entra-Benutzer, mit dem sich das Skript vor dem Seeden einloggt (POST
    /api/auth/mock/login). Muss GovernanceAdmin-Rechte haben. Default "admin@platform.example"
    (siehe scripts/reset-cosmos-dev-data.ps1, das diesen Benutzer nach einem Reset anlegt).

.PARAMETER GuestCount
    Anzahl zu erzeugender Gaeste. Default 500, maximal 5000 (serverseitig begrenzt).

.PARAMETER WorkloadName
    Optionaler eigener Workload-Name statt des Default-Demo-Namens.

.EXAMPLE
    ./scripts/seed-large-workload.ps1
    # 500 Gaeste, Default-Workload, gegen http://localhost:5000, Login als admin@platform.example.

.EXAMPLE
    ./scripts/seed-large-workload.ps1 -GuestCount 1500 -WorkloadName "Onboarding-Projekt Nord"
#>
param(
    [string]$ApiBaseUrl,
    [string]$PlatformTenantId = "dev-tenant-a",
    [string]$AdminMail = "admin@platform.example",
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

Write-Host "Seede $GuestCount Gaeste + 1 Workload gegen $ApiBaseUrl (Login als $AdminMail)..." -ForegroundColor Cyan

try {
    $health = Invoke-RestMethod -Uri "$ApiBaseUrl/health" -ErrorAction Stop
    if ($health.mode -ne "LOCAL_MOCK") {
        throw "API laeuft im Modus '$($health.mode)', nicht LOCAL_MOCK - der Seed-Endpoint ist nur unter LOCAL_MOCK registriert."
    }
}
catch {
    throw "Portal API unter $ApiBaseUrl nicht erreichbar oder nicht im LOCAL_MOCK-Modus. Starte sie zuerst (siehe README Quick Start). Fehler: $($_.Exception.Message)"
}

# Erweiterung 2026-08-30 (Teil 3): freie X-Platform-Tenant-Id-Header wurden durch ein JWT
# Bearer Token ersetzt - der Tenant kommt jetzt aus dem Token-Claim, nicht mehr aus
# -PlatformTenantId (siehe Parameterbeschreibung oben und ClaimsTenantContextAccessor.cs).
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

$body = @{ guestCount = $GuestCount }
if ($WorkloadName) { $body.workloadName = $WorkloadName }

$headers = @{ "Authorization" = "Bearer $($login.token)"; "Content-Type" = "application/json" }
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
Write-Host "Ansehen: Web-UI unter Guest Pool / Workloads (Admin-Ansicht), oder direkt (Login-Token noetig):" -ForegroundColor Cyan
Write-Host "  `$token = (Invoke-RestMethod -Method Post -Uri '$ApiBaseUrl/api/auth/mock/login' -ContentType 'application/json' -Body (@{ mail = '$AdminMail' } | ConvertTo-Json)).token"
Write-Host "  Invoke-RestMethod -Headers @{ Authorization = `"Bearer `$token`" } $ApiBaseUrl/api/guest-accounts"
Write-Host "  Invoke-RestMethod -Headers @{ Authorization = `"Bearer `$token`" } $ApiBaseUrl/api/workloads"
