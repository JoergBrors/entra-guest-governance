<#
.SYNOPSIS
    Prueft und richtet die lokale Development-Umgebung fuer LOCAL_MOCK ein: Runtimes/Tools,
    freie Ports, sowie optional den lokalen Azure Cosmos DB Emulator.

.DESCRIPTION
    Dieses Skript erstellt KEINE Azure-Cloud-Ressourcen. Es arbeitet ausschliesslich lokal:
      1. Pruefstatus fuer .NET SDK, Node.js/npm, Bicep CLI, Azure CLI (optional), den
         Azure Cosmos DB Emulator (Windows local) und Azurite (lokaler Azure Storage
         Emulator) -> Statusbericht (OK / FEHLT / Version zu alt).
      2. Mit -Install: fehlende Tools werden per winget/npm nachinstalliert (Ausnahme:
         Cosmos DB Emulator, siehe -InstallCosmosEmulator; .NET SDK-Version ist ueber
         global.json fixiert und wird nicht automatisch hochgezogen, wenn ein neueres SDK
         bereits ausreicht).
      3. Ermittelt freie Ports fuer API (Default 5000) und Web (aus vite.config.ts gelesen,
         Default 5301). Bei Belegung wird automatisch der naechste freie Port gewaehlt und
         nach .env.local geschrieben (API_BASE_URL/WEB_BASE_URL) sowie vite.config.ts
         (server.port) aktualisiert. Ist der blockierende Prozess erkennbar ein eigener,
         noch laufender node/dotnet-Prozess aus diesem Repository, wird das mit PID und
         Kommandozeile angezeigt (kein automatisches Beenden — siehe Abschnitt "Bekannte
         Fallstricke" unten).
      4. Mit -InitCosmosEmulator: prueft ob der Cosmos DB Emulator installiert ist, startet
         ihn bei Bedarf (ueber das mitgelieferte PowerShell-Modul) und legt die im Bicep-Modul
         (infra/modules/cosmos-free-tier.bicep) definierte Datenbank/Container-Struktur
         (b2b-governance-dev / domain / discovery / jobs / audit) per REST gegen den lokalen Emulator-Endpoint
         an. Verwendet dabei ausschliesslich den oeffentlich dokumentierten Well-Known-
         Emulator-Key - kein echtes Secret. Der Connection String wird immer (auch ohne
         -InitCosmosEmulator) als Platzhalter-Struktur nach .env.local geschrieben.
      5. Mit -InitStorageEmulator: prueft ob Azurite (npm-Paket) installiert ist, startet es
         bei Bedarf als Hintergrundprozess (Blob/Queue/Table auf den Standardports
         10000/10001/10002) und schreibt den well-known Azurite-Connection-String
         (AccountName devstoreaccount1, oeffentlich dokumentierter Key) nach .env.local.

.PARAMETER Install
    Installiert fehlende Runtimes/Tools (dotnet SDK, Node.js, Bicep CLI, Azure CLI, Azurite)
    per winget/npm nach, statt nur den Fehlbestand zu melden.

.PARAMETER InitCosmosEmulator
    Startet den lokalen Cosmos DB Emulator (falls installiert, sonst Warnung) und legt
    Datenbank + Container gemaess infra/modules/cosmos-free-tier.bicep lokal an.

.PARAMETER InstallCosmosEmulator
    Installiert den Cosmos DB Emulator per winget, falls er fehlt. Nur wirksam zusammen mit
    -Install oder -InitCosmosEmulator.

.PARAMETER InitStorageEmulator
    Startet Azurite (falls installiert, sonst Warnung) im Hintergrund und schreibt den
    well-known Connection String nach .env.local.

.PARAMETER InstallStorageEmulator
    Installiert Azurite per "npm install -g azurite", falls es fehlt. Nur wirksam zusammen
    mit -Install oder -InitStorageEmulator.

.PARAMETER ApiPort
    Bevorzugter Port fuer B2B.Portal.Api. Default 5000.

.PARAMETER WebPortFallback
    Bevorzugter Port fuer B2B.Portal.Web, falls er nicht aus vite.config.ts gelesen werden
    kann. Default 5301.

.EXAMPLE
    ./scripts/requirements.ps1
    # Nur pruefen: Tools, Ports, Emulator-Installationsstatus. Aendert nichts ausser .env.local
    # (Ports + Emulator-Connection-String-Platzhalter).

.EXAMPLE
    ./scripts/requirements.ps1 -Install -InitCosmosEmulator -InitStorageEmulator -InstallCosmosEmulator -InstallStorageEmulator
    # Fehlende Tools nachinstallieren, freie Ports ermitteln + in .env.local/vite.config.ts
    # eintragen, Cosmos DB Emulator + Azurite starten und lokale Struktur/Connection Strings
    # anlegen.

.NOTES
    Erstellt/aendert ausschliesslich lokale Konfiguration (.env.local, vite.config.ts) und
    lokal laufende Prozesse (Cosmos DB Emulator, Azurite). Keine Azure-Subscription, kein
    az login, keine Cloud-Ressourcen noetig.

    Bekannte Fallstricke (siehe docs/prompts fuer den vollen Vorfallbericht): Wenn ein
    vorheriger "npm run dev" (oder ein zweiter Aufruf dieses Skripts) noch aktiv laeuft,
    bleibt der davon belegte Port belegt, auch nachdem dieses Skript einen neuen Port in
    vite.config.ts eingetragen hat — Vite muss dafuer neu gestartet werden. Das Skript
    kann einen bereits laufenden Dev-Server nicht "reparieren", zeigt aber ab dieser
    Version an, welcher Prozess (PID, Kommandozeile) einen konfigurierten Port blockiert.
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$InitCosmosEmulator,
    [switch]$InstallCosmosEmulator,
    [switch]$InitStorageEmulator,
    [switch]$InstallStorageEmulator,
    [int]$ApiPort = 5000,
    [int]$WebPortFallback = 5301
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$results = @()

function Add-Result {
    param([string]$Name, [string]$Status, [string]$Detail)
    $script:results += [pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail }
}

function Test-CommandVersion {
    param([string]$Command, [string]$VersionArg = "--version")
    try {
        $out = & $Command $VersionArg 2>$null
        return ($out | Select-Object -First 1)
    }
    catch {
        return $null
    }
}

function Install-WithWinget {
    param([string]$WingetId, [string]$FriendlyName)
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Warning "winget nicht verfuegbar - $FriendlyName kann nicht automatisch installiert werden. Bitte manuell installieren."
        return $false
    }
    Write-Host "Installiere $FriendlyName per winget ($WingetId)..." -ForegroundColor Cyan
    winget install --id $WingetId --silent --accept-package-agreements --accept-source-agreements
    return $LASTEXITCODE -eq 0
}

# ---------------------------------------------------------------------------
# 1. Runtimes / Tools
# ---------------------------------------------------------------------------
Write-Host "=== 1. Runtimes und Tools ===" -ForegroundColor Cyan

# .NET SDK — Version aus global.json als Mindestanforderung
$globalJsonPath = Join-Path $RepoRoot "global.json"
$requiredSdk = "10.0.100"
if (Test-Path $globalJsonPath) {
    $requiredSdk = (Get-Content $globalJsonPath | ConvertFrom-Json).sdk.version
}

$dotnetVersion = Test-CommandVersion -Command "dotnet"
if ($dotnetVersion) {
    $installedSdks = & dotnet --list-sdks 2>$null
    $hasMatching = $installedSdks | Where-Object { $_ -match [regex]::Escape($requiredSdk.Substring(0, 4)) }
    if ($hasMatching) {
        Add-Result ".NET SDK" "OK" "aktiv: $dotnetVersion (benoetigt >= $requiredSdk, siehe global.json)"
    }
    else {
        Add-Result ".NET SDK" "VERSION" "aktiv: $dotnetVersion, aber kein SDK $($requiredSdk.Substring(0,4)).x installiert (siehe global.json)"
        if ($Install) { Install-WithWinget -WingetId "Microsoft.DotNet.SDK.10" -FriendlyName ".NET 10 SDK" | Out-Null }
    }
}
else {
    Add-Result ".NET SDK" "FEHLT" "dotnet CLI nicht gefunden"
    if ($Install) { Install-WithWinget -WingetId "Microsoft.DotNet.SDK.10" -FriendlyName ".NET 10 SDK" | Out-Null }
}

# Node.js / npm
$nodeVersion = Test-CommandVersion -Command "node"
if ($nodeVersion) {
    Add-Result "Node.js" "OK" "aktiv: $nodeVersion"
}
else {
    Add-Result "Node.js" "FEHLT" "node nicht gefunden"
    if ($Install) { Install-WithWinget -WingetId "OpenJS.NodeJS.LTS" -FriendlyName "Node.js LTS" | Out-Null }
}

$npmVersion = Test-CommandVersion -Command "npm"
if ($npmVersion) {
    Add-Result "npm" "OK" "aktiv: $npmVersion"
}
else {
    Add-Result "npm" "FEHLT" "npm nicht gefunden (wird normalerweise mit Node.js installiert)"
}

# Bicep CLI (ueber az bicep, wie im Projekt bereits verwendet)
$azVersion = Test-CommandVersion -Command "az"
if ($azVersion) {
    Add-Result "Azure CLI" "OK" "aktiv"
    try {
        $bicepVersion = & az bicep version 2>$null
        if ($bicepVersion) {
            Add-Result "Bicep CLI" "OK" "$bicepVersion"
        }
        else {
            Add-Result "Bicep CLI" "FEHLT" "az bicep nicht installiert"
            if ($Install) {
                Write-Host "Installiere Bicep CLI ueber az bicep install..." -ForegroundColor Cyan
                az bicep install
            }
        }
    }
    catch {
        Add-Result "Bicep CLI" "FEHLT" "az bicep version fehlgeschlagen"
    }
}
else {
    Add-Result "Azure CLI" "FEHLT" "az nicht gefunden (optional fuer LOCAL_MOCK, noetig fuer Bicep-Validierung/-Deploy)"
    if ($Install) { Install-WithWinget -WingetId "Microsoft.AzureCLI" -FriendlyName "Azure CLI" | Out-Null }
}

# Microsoft.Graph PowerShell SDK (fuer scripts/setup-entra-app.ps1)
if (Get-Module -ListAvailable -Name Microsoft.Graph.Applications) {
    Add-Result "Microsoft.Graph PowerShell" "OK" "installiert (fuer scripts/setup-entra-app.ps1)"
}
else {
    Add-Result "Microsoft.Graph PowerShell" "FEHLT" "optional, nur fuer DEV_INTEGRATION noetig. Install-Module Microsoft.Graph -Scope CurrentUser"
}

# Cosmos DB Emulator (Windows local) — nur Installationsstatus, Start erfolgt in Abschnitt 3
$cosmosEmulatorPath = "$env:ProgramFiles\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe"
$cosmosInstalled = Test-Path $cosmosEmulatorPath
if ($cosmosInstalled) {
    Add-Result "Cosmos DB Emulator" "OK" "installiert unter $cosmosEmulatorPath"
}
else {
    Add-Result "Cosmos DB Emulator" "FEHLT" "nicht installiert (nur noetig fuer -InitCosmosEmulator; LOCAL_MOCK selbst nutzt InMemory-Repositories)"
    if ($Install -or $InstallCosmosEmulator) {
        $installed = Install-WithWinget -WingetId "Microsoft.Azure.CosmosEmulator" -FriendlyName "Azure Cosmos DB Emulator"
        if ($installed) { $cosmosInstalled = Test-Path $cosmosEmulatorPath }
    }
}

# Azurite (lokaler Azure Storage Emulator, npm-Paket) — nur Installationsstatus, Start in Abschnitt 4
$azuriteInstalled = [bool](Get-Command azurite -ErrorAction SilentlyContinue)
if ($azuriteInstalled) {
    $azuriteVersion = Test-CommandVersion -Command "azurite"
    Add-Result "Azurite (Storage Emulator)" "OK" "aktiv: $azuriteVersion"
}
else {
    Add-Result "Azurite (Storage Emulator)" "FEHLT" "nicht installiert (nur noetig fuer -InitStorageEmulator; LOCAL_MOCK selbst nutzt keinen Blob/Queue-Storage)"
    if ($Install -or $InstallStorageEmulator) {
        if (Get-Command npm -ErrorAction SilentlyContinue) {
            Write-Host "Installiere Azurite per npm install -g azurite..." -ForegroundColor Cyan
            npm install -g azurite
            $azuriteInstalled = [bool](Get-Command azurite -ErrorAction SilentlyContinue)
        }
        else {
            Write-Warning "npm nicht verfuegbar - Azurite kann nicht installiert werden."
        }
    }
}

# ---------------------------------------------------------------------------
# 2. Freie Ports ermitteln + Konfiguration schreiben
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== 2. Ports ===" -ForegroundColor Cyan

function Test-PortFree {
    param([int]$Port)
    $inUse = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    return -not $inUse
}

function Get-PortOwnerInfo {
    # Liefert eine kurze Beschreibung des Prozesses, der aktuell auf $Port lauscht (falls
    # ermittelbar) - hilft zu erkennen, ob es sich um einen noch laufenden eigenen
    # npm/dotnet-Dev-Prozess dieses Repos handelt (siehe "Bekannte Fallstricke" im Hilfetext).
    param([int]$Port)
    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $conn) { return $null }
    try {
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$($conn.OwningProcess)" -ErrorAction Stop
        $isOwnRepo = $proc.CommandLine -and ($proc.CommandLine -like "*$RepoRoot*" -or $proc.CommandLine -like "*B2B.Portal*" -or $proc.CommandLine -like "*vite*")
        return [pscustomobject]@{
            Pid          = $conn.OwningProcess
            Name         = $proc.Name
            CommandLine  = $proc.CommandLine
            LikelyOwnRepo = [bool]$isOwnRepo
        }
    }
    catch {
        return [pscustomobject]@{ Pid = $conn.OwningProcess; Name = "(unbekannt)"; CommandLine = $null; LikelyOwnRepo = $false }
    }
}

function Find-FreePort {
    param([int]$PreferredPort, [int]$MaxAttempts = 50)
    for ($p = $PreferredPort; $p -lt ($PreferredPort + $MaxAttempts); $p++) {
        if (Test-PortFree -Port $p) { return $p }
    }
    throw "Kein freier Port im Bereich $PreferredPort-$($PreferredPort + $MaxAttempts) gefunden."
}

# Aktuellen Web-Port aus vite.config.ts lesen (Fallback: Parameter)
$viteConfigPath = Join-Path $RepoRoot "src/B2B.Portal.Web/vite.config.ts"
$currentWebPort = $WebPortFallback
if (Test-Path $viteConfigPath) {
    $content = Get-Content $viteConfigPath -Raw
    if ($content -match 'port:\s*(\d+)') {
        $currentWebPort = [int]$Matches[1]
    }
}

function Resolve-Port {
    param([int]$PreferredPort, [string]$Label)
    if (Test-PortFree -Port $PreferredPort) {
        Add-Result $Label "OK" "$PreferredPort frei"
        return $PreferredPort
    }

    $owner = Get-PortOwnerInfo -Port $PreferredPort
    $ownerDesc = if ($owner) {
        $repoNote = if ($owner.LikelyOwnRepo) { " — sieht nach einem laufenden Prozess DIESES Repos aus (npm run dev / dotnet run), ggf. vorher beenden" } else { " — gehoert vermutlich nicht zu diesem Repo" }
        "PID $($owner.Pid) ($($owner.Name))$repoNote"
    }
    else { "Prozess nicht ermittelbar" }

    $newPort = Find-FreePort -PreferredPort ($PreferredPort + 1)
    Add-Result $Label "GEAENDERT" "$PreferredPort belegt durch $ownerDesc -> $newPort gewaehlt"
    Write-Warning "$Label $PreferredPort ist belegt: $ownerDesc"
    return $newPort
}

$finalApiPort = Resolve-Port -PreferredPort $ApiPort -Label "API-Port"
$finalWebPort = Resolve-Port -PreferredPort $currentWebPort -Label "Web-Port"

# vite.config.ts aktualisieren, falls sich der Web-Port geaendert hat
if ($finalWebPort -ne $currentWebPort -and (Test-Path $viteConfigPath)) {
    $content = Get-Content $viteConfigPath -Raw
    $content = $content -replace 'port:\s*\d+', "port: $finalWebPort"
    Set-Content -Path $viteConfigPath -Value $content -NoNewline
    Write-Host "vite.config.ts aktualisiert: server.port = $finalWebPort" -ForegroundColor Green
}

# .env.local schreiben/aktualisieren (API_BASE_URL, WEB_BASE_URL)
$envLocalPath = Join-Path $RepoRoot ".env.local"
$envExamplePath = Join-Path $RepoRoot ".env.example"
$envLines = if (Test-Path $envLocalPath) { Get-Content $envLocalPath } elseif (Test-Path $envExamplePath) { Get-Content $envExamplePath } else { @() }

function Set-EnvLine {
    param([string[]]$Lines, [string]$Key, [string]$Value)
    $newLine = "$Key=$Value"
    $idx = -1
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match "^$Key=") { $idx = $i; break }
    }
    if ($idx -ge 0) { $Lines[$idx] = $newLine } else { $Lines += $newLine }
    return $Lines
}

$envLines = Set-EnvLine -Lines $envLines -Key "API_BASE_URL" -Value "http://localhost:$finalApiPort"
$envLines = Set-EnvLine -Lines $envLines -Key "WEB_BASE_URL" -Value "http://localhost:$finalWebPort"

# ASPNETCORE_URLS wird von .vscode/launch.json ("Portal API") ueber envFile=.env.local
# geladen, damit der VS-Code-Debug-Start denselben Port verwendet wie hier ermittelt -
# ohne diesen Eintrag wuerde .NET auf seinen eigenen Default (https://localhost:7xxx /
# http://localhost:5xxx aus launchSettings.json) zurueckfallen statt auf $finalApiPort.
$envLines = Set-EnvLine -Lines $envLines -Key "ASPNETCORE_URLS" -Value "http://localhost:$finalApiPort"

# Connection-String-Platzhalter fuer Cosmos DB Emulator / Azurite immer als Struktur
# anlegen (Werte werden erst bei -InitCosmosEmulator/-InitStorageEmulator scharf gesetzt),
# damit ihr Ursprung/Zweck auch ohne Emulator-Init sichtbar ist (siehe .env.example).
if (-not ($envLines -match "^COSMOS_EMULATOR_ENDPOINT=")) {
    $envLines = Set-EnvLine -Lines $envLines -Key "COSMOS_EMULATOR_ENDPOINT" -Value ""
    $envLines = Set-EnvLine -Lines $envLines -Key "COSMOS_EMULATOR_KEY" -Value ""
    $envLines = Set-EnvLine -Lines $envLines -Key "COSMOS_DATABASE_ID" -Value ""
}
if (-not ($envLines -match "^AZURE_STORAGE_CONNECTION_STRING=")) {
    $envLines = Set-EnvLine -Lines $envLines -Key "AZURE_STORAGE_CONNECTION_STRING" -Value ""
}

Set-Content -Path $envLocalPath -Value $envLines
Write-Host ".env.local aktualisiert: API_BASE_URL=http://localhost:$finalApiPort, WEB_BASE_URL=http://localhost:$finalWebPort" -ForegroundColor Green

# Web-eigenes .env.local (VITE_API_BASE_URL) ebenfalls konsistent halten
$webEnvLocalPath = Join-Path $RepoRoot "src/B2B.Portal.Web/.env.local"
$webEnvExamplePath = Join-Path $RepoRoot "src/B2B.Portal.Web/.env.example"
$webEnvLines = if (Test-Path $webEnvLocalPath) { Get-Content $webEnvLocalPath } elseif (Test-Path $webEnvExamplePath) { Get-Content $webEnvExamplePath } else { @() }
$webEnvLines = Set-EnvLine -Lines $webEnvLines -Key "VITE_API_BASE_URL" -Value "http://localhost:$finalApiPort"
Set-Content -Path $webEnvLocalPath -Value $webEnvLines
Write-Host "src/B2B.Portal.Web/.env.local aktualisiert: VITE_API_BASE_URL=http://localhost:$finalApiPort" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 3. Cosmos DB Emulator initialisieren (optional)
# ---------------------------------------------------------------------------
if ($InitCosmosEmulator) {
    Write-Host ""
    Write-Host "=== 3. Cosmos DB Emulator ===" -ForegroundColor Cyan

    if (-not $cosmosInstalled) {
        Write-Warning "Cosmos DB Emulator ist nicht installiert. Ueberspringe Initialisierung. " +
            "Installiere ihn mit -InstallCosmosEmulator oder manuell: https://aka.ms/cosmosdb-emulator"
        Add-Result "Cosmos DB Emulator Init" "UEBERSPRUNGEN" "Emulator nicht installiert"
    }
    else {
        # Well-known, oeffentlich dokumentierter Emulator-Key (kein echtes Secret, gilt fuer
        # jede lokale Emulator-Installation gleichermassen, siehe Microsoft-Dokumentation
        # "What is the Azure Cosmos DB emulator? > Authentication").
        $emulatorEndpoint = "https://localhost:8081"
        $emulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

        $psModulePath = "$env:ProgramFiles\Azure Cosmos DB Emulator\PSModules\Microsoft.Azure.CosmosDB.Emulator"
        $emulatorRunning = $false
        if (Test-Path $psModulePath) {
            Import-Module $psModulePath -ErrorAction SilentlyContinue
            $status = Get-CosmosDbEmulatorStatus -ErrorAction SilentlyContinue
            if ($status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
                $emulatorRunning = $true
                Write-Host "Cosmos DB Emulator laeuft bereits." -ForegroundColor Green
            }
            else {
                Write-Host "Starte Cosmos DB Emulator (kann beim ersten Start einige Minuten dauern)..." -ForegroundColor Cyan
                Start-CosmosDbEmulator -ErrorAction Stop
                $emulatorRunning = $true
            }
        }
        else {
            # Fallback ohne PS-Modul: direkt ueber die Exe starten und auf GetStatus pollen.
            & $cosmosEmulatorPath /GetStatus | Out-Null
            if ($LASTEXITCODE -ne 2) {
                Write-Host "Starte Cosmos DB Emulator (Exe-Fallback)..." -ForegroundColor Cyan
                Start-Process -FilePath $cosmosEmulatorPath -ArgumentList "/NoUI" | Out-Null
                $maxWaitSeconds = 120
                $waited = 0
                do {
                    Start-Sleep -Seconds 5
                    $waited += 5
                    & $cosmosEmulatorPath /GetStatus | Out-Null
                } while ($LASTEXITCODE -ne 2 -and $waited -lt $maxWaitSeconds)
            }
            $emulatorRunning = ($LASTEXITCODE -eq 2)
        }

        if (-not $emulatorRunning) {
            Add-Result "Cosmos DB Emulator Init" "FEHLER" "Emulator konnte nicht gestartet werden"
        }
        else {
            Add-Result "Cosmos DB Emulator Init" "OK" "laeuft unter $emulatorEndpoint"

            # --- REST-Aufrufe gegen den Emulator (HMAC-Signatur nach offiziellem Cosmos DB
            # REST-API-Schema: StringToSign = "{verb}\n{resourceType}\n{resourceLink}\n{date}\n\n") ---
            function Invoke-CosmosEmulatorRequest {
                param(
                    [string]$Method,
                    [string]$ResourceType,
                    [string]$ResourceLink,
                    [string]$Path,
                    [hashtable]$Body = $null
                )
                $utcDate = [DateTime]::UtcNow.ToString("r")
                $verb = $Method.ToLowerInvariant()
                $resType = $ResourceType.ToLowerInvariant()
                $resLink = $ResourceLink
                $stringToSign = "$verb`n$resType`n$resLink`n$($utcDate.ToLowerInvariant())`n`n"

                $keyBytes = [Convert]::FromBase64String($emulatorKey)
                $hmac = New-Object System.Security.Cryptography.HMACSHA256
                $hmac.Key = $keyBytes
                $sigBytes = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stringToSign))
                $signature = [Convert]::ToBase64String($sigBytes)
                $authRaw = "type=master&ver=1.0&sig=$signature"
                $authEncoded = [Uri]::EscapeDataString($authRaw)

                $headers = @{
                    "x-ms-date"    = $utcDate
                    "x-ms-version" = "2018-12-31"
                    "Authorization" = $authEncoded
                }

                $uri = "$emulatorEndpoint/$Path"
                $params = @{
                    Method      = $Method
                    Uri         = $uri
                    Headers     = $headers
                    ContentType = "application/json"
                    SkipCertificateCheck = $true
                    ErrorAction = "Stop"
                }
                if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 5) }

                return Invoke-RestMethod @params
            }

            # Fuenf logisch getrennte Container statt eines gemeinsamen "domain-data"-
            # Containers: Desired State (domain) / Actual State (discovery) / Mock-Entra-
            # Verzeichnis (entraid) / Job-Queue (jobs) / Audit (audit) — spiegelt
            # infra/modules/cosmos-free-tier.bicep exakt.
            $databaseId = "b2b-governance-dev"
            $containers = @(
                @{ id = "domain"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } }
                @{ id = "discovery"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } }
                @{ id = "entraid"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } }
                @{ id = "jobs"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" }; defaultTtl = -1 }
                @{ id = "audit"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" }; defaultTtl = -1 }
            )

            try {
                try {
                    Invoke-CosmosEmulatorRequest -Method "POST" -ResourceType "dbs" -ResourceLink "" -Path "dbs" `
                        -Body @{ id = $databaseId } | Out-Null
                    Write-Host "Datenbank '$databaseId' angelegt." -ForegroundColor Green
                }
                catch {
                    if ($_.Exception.Response.StatusCode.value__ -eq 409) {
                        Write-Host "Datenbank '$databaseId' existiert bereits." -ForegroundColor Yellow
                    }
                    else { throw }
                }

                foreach ($c in $containers) {
                    try {
                        Invoke-CosmosEmulatorRequest -Method "POST" -ResourceType "colls" -ResourceLink "dbs/$databaseId" `
                            -Path "dbs/$databaseId/colls" -Body $c | Out-Null
                        Write-Host "Container '$($c.id)' angelegt." -ForegroundColor Green
                    }
                    catch {
                        if ($_.Exception.Response.StatusCode.value__ -eq 409) {
                            Write-Host "Container '$($c.id)' existiert bereits." -ForegroundColor Yellow
                        }
                        else { throw }
                    }
                }
                Add-Result "Cosmos DB Struktur" "OK" "Datenbank '$databaseId' + Container domain/discovery/entraid/jobs/audit vorhanden (siehe infra/modules/cosmos-free-tier.bicep)"
            }
            catch {
                Add-Result "Cosmos DB Struktur" "FEHLER" $_.Exception.Message
            }

            # Emulator-Verbindungsdaten in .env.local eintragen (nur lokal, well-known key).
            $envLines = Get-Content $envLocalPath
            $envLines = Set-EnvLine -Lines $envLines -Key "COSMOS_EMULATOR_ENDPOINT" -Value $emulatorEndpoint
            $envLines = Set-EnvLine -Lines $envLines -Key "COSMOS_EMULATOR_KEY" -Value $emulatorKey
            $envLines = Set-EnvLine -Lines $envLines -Key "COSMOS_DATABASE_ID" -Value $databaseId
            Set-Content -Path $envLocalPath -Value $envLines
            Write-Host ".env.local um COSMOS_EMULATOR_* ergaenzt (Well-Known-Key, kein echtes Secret)." -ForegroundColor Green
        }
    }
}
else {
    Write-Host ""
    Write-Host "Cosmos DB Emulator wird nicht initialisiert (siehe -InitCosmosEmulator)." -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# 4. Azurite (Azure Storage Emulator) initialisieren (optional)
# ---------------------------------------------------------------------------
if ($InitStorageEmulator) {
    Write-Host ""
    Write-Host "=== 4. Azurite (Azure Storage Emulator) ===" -ForegroundColor Cyan

    if (-not $azuriteInstalled) {
        Write-Warning "Azurite ist nicht installiert. Ueberspringe Initialisierung. Installiere es mit -InstallStorageEmulator oder manuell: npm install -g azurite"
        Add-Result "Azurite Init" "UEBERSPRUNGEN" "Azurite nicht installiert"
    }
    else {
        # Well-known Azurite-Account/-Key (identisch zum frueheren Azure Storage Emulator,
        # oeffentlich dokumentiert, kein echtes Secret): siehe Microsoft-Dokumentation
        # "Connect to Azurite with SDKs and tools > Use a well-known storage account and key".
        $azuriteAccountName = "devstoreaccount1"
        $azuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="
        $azuriteBlobPort = 10000
        $azuriteQueuePort = 10001
        $azuriteTablePort = 10002

        $azuriteRunning = -not (Test-PortFree -Port $azuriteBlobPort)
        if ($azuriteRunning) {
            Write-Host "Azurite laeuft bereits (Port $azuriteBlobPort belegt)." -ForegroundColor Green
        }
        else {
            $azuriteDataDir = Join-Path $RepoRoot ".azurite"
            New-Item -ItemType Directory -Force -Path $azuriteDataDir | Out-Null
            Write-Host "Starte Azurite im Hintergrund (Datenverzeichnis: $azuriteDataDir)..." -ForegroundColor Cyan
            # azurite ist unter Windows ein npm-generierter .cmd-Shim - Start-Process kann
            # solche Shims mit direktem -FilePath unzuverlaessig starten (Prozess kehrt
            # sofort zurueck, ohne dass der eigentliche Node-Prozess je hochkommt). Ueber
            # cmd.exe /c aufrufen, damit das Shim korrekt aufgeloest und ausgefuehrt wird.
            Start-Process -FilePath "cmd.exe" `
                -ArgumentList "/c", "azurite --silent --location `"$azuriteDataDir`" --debug `"$azuriteDataDir\debug.log`"" `
                -WindowStyle Hidden | Out-Null

            $maxWaitSeconds = 30
            $waited = 0
            while ((Test-PortFree -Port $azuriteBlobPort) -and $waited -lt $maxWaitSeconds) {
                Start-Sleep -Seconds 2
                $waited += 2
            }
            $azuriteRunning = -not (Test-PortFree -Port $azuriteBlobPort)
        }

        if (-not $azuriteRunning) {
            Add-Result "Azurite Init" "FEHLER" "Azurite konnte nicht gestartet werden (Port $azuriteBlobPort weiterhin frei)"
        }
        else {
            Add-Result "Azurite Init" "OK" "laeuft auf Ports $azuriteBlobPort/$azuriteQueuePort/$azuriteTablePort"

            $connectionString = "DefaultEndpointsProtocol=http;AccountName=$azuriteAccountName;AccountKey=$azuriteAccountKey;" +
                "BlobEndpoint=http://127.0.0.1:$azuriteBlobPort/$azuriteAccountName;" +
                "QueueEndpoint=http://127.0.0.1:$azuriteQueuePort/$azuriteAccountName;" +
                "TableEndpoint=http://127.0.0.1:$azuriteTablePort/$azuriteAccountName;"

            $envLines = Get-Content $envLocalPath
            $envLines = Set-EnvLine -Lines $envLines -Key "AZURE_STORAGE_CONNECTION_STRING" -Value $connectionString
            Set-Content -Path $envLocalPath -Value $envLines
            Write-Host ".env.local um AZURE_STORAGE_CONNECTION_STRING ergaenzt (Well-Known-Key, kein echtes Secret)." -ForegroundColor Green
        }
    }
}
else {
    Write-Host ""
    Write-Host "Azurite wird nicht initialisiert (siehe -InitStorageEmulator)." -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Zusammenfassung
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Zusammenfassung ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize

$missing = $results | Where-Object { $_.Status -in @("FEHLT", "FEHLER") }
if ($missing) {
    Write-Host ""
    Write-Host "Es fehlen $($missing.Count) Voraussetzung(en). Fuehre mit -Install (und ggf. -InitCosmosEmulator -InstallCosmosEmulator) aus, um nachzuinstallieren." -ForegroundColor Yellow
}
else {
    Write-Host ""
    Write-Host "Alle geprueften Voraussetzungen sind erfuellt." -ForegroundColor Green
}

Write-Host ""
Write-Host "Naechster Schritt: dotnet build -c Debug && dotnet test -c Debug, dann API/Worker/Web starten (siehe README Quick Start)." -ForegroundColor Cyan
