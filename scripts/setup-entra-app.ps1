<#
.SYNOPSIS
    Legt die Entra-ID-Voraussetzungen fuer DEV_INTEGRATION an: App Registration,
    Client Secret und die im Blueprint/MVP-Dokument benoetigten Microsoft Graph
    Application Permissions (Mail.Send, User.Invite.All, Group.ReadWrite.All).

.DESCRIPTION
    Dieses Skript erstellt KEINE Azure-Ressourcen (Compute/Storage/etc.) - es arbeitet
    ausschliesslich gegen Microsoft Graph (Entra ID) ueber das Microsoft.Graph PowerShell
    SDK. Es ist der Graph-Gegenpart zu infra/*.bicep, das bewusst separat bleibt (Blueprint
    Abschnitt "Nicht festgelegt" / README "Drei Development-Modi").

    Standardmaessig laeuft das Skript im WhatIf-Modus (-WhatIf), zeigt also nur an, was
    angelegt wuerde. Erst mit explizitem -Apply wird tatsaechlich in Entra ID geschrieben.

    Benoetigte Delegated-Permission fuer den ausfuehrenden Benutzer/die ausfuehrende
    Session: Application.ReadWrite.All (um eine App Registration + Service Principal
    anzulegen) und AppRoleAssignment.ReadWrite.All (um Application Permissions zu
    gewaehren/admin-consent-en). Das Skript fordert KEINEN eigenstaendigen Consent-Flow an,
    sondern nutzt die interaktive Graph-PowerShell-Anmeldung des aufrufenden Admins.

.PARAMETER DisplayName
    Anzeigename der App Registration, z.B. "B2B-Guest-Governance-Dev".

.PARAMETER Apply
    Ohne diesen Switch laeuft das Skript im Dry-Run (WhatIf) und aendert nichts in Entra ID.

.PARAMETER WriteEnvLocal
    Schreibt die erzeugten Werte (Tenant-, Client-ID, Secret) zusaetzlich in .env.local
    im Repository-Root. .env.local ist in .gitignore ausgeschlossen und wird NIE committed.
    Ohne diesen Switch werden die Werte nur auf der Konsole ausgegeben.

.EXAMPLE
    ./scripts/setup-entra-app.ps1
    # Dry-Run: zeigt an, welche App Registration + Permissions angelegt wuerden.

.EXAMPLE
    ./scripts/setup-entra-app.ps1 -Apply -WriteEnvLocal
    # Legt die App Registration inkl. Secret an und schreibt die Werte nach .env.local.

.NOTES
    - Es werden KEINE echten Tenant-/Client-IDs im Repository hinterlegt (siehe
      .env.example). Dieses Skript erzeugt sie zur Laufzeit gegen den Tenant, mit dem
      Connect-MgGraph verbunden ist - typischerweise ein dedizierter Entra Dev-Tenant.
    - Admin Consent fuer Application Permissions muss von einem Global Administrator /
      Privileged Role Administrator des Ziel-Tenants erteilt werden. Das Skript versucht
      dies automatisiert (New-MgServicePrincipalAppRoleAssignment), was entsprechende
      Admin-Rechte der angemeldeten Session voraussetzt.
    - Client Secret wird nur einmalig angezeigt/geschrieben - Microsoft Graph liefert
      den Klartextwert ausschliesslich beim Erstellen zurueck.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$DisplayName = "B2B-Guest-Governance-Dev",
    [switch]$Apply,
    [switch]$WriteEnvLocal,
    [string]$SecretDescription = "b2b-portal-dev-secret",
    [int]$SecretValidityMonths = 6
)

$ErrorActionPreference = "Stop"

# Graph Application Permissions, die laut Blueprint/Development-Dokument fuer
# DEV_INTEGRATION benoetigt werden (siehe README "Drei Development-Modi",
# src/B2B.Portal.Infrastructure/Email/EmailProviders.cs Kommentar Zeile 58).
$RequiredGraphAppRoles = @(
    @{ Name = "User.Invite.All";      Reason = "B2B-Gasteinladung (InviteGuestAsync)" }
    @{ Name = "Mail.Send";            Reason = "Shared-Mailbox Notification (GraphSharedMailboxEmailProvider)" }
    @{ Name = "Group.ReadWrite.All";  Reason = "Workload-Rollen-Mapping ueber Security Groups" }
    @{ Name = "User.Read.All";        Reason = "Discovery/Reconciliation-Handler: bestehende Gaeste/Attribute lesen" }
)

$GraphResourceAppId = "00000003-0000-0000-c000-000000000000" # Microsoft Graph

function Assert-GraphModule {
    if (-not (Get-Module -ListAvailable -Name Microsoft.Graph.Applications)) {
        throw "Microsoft.Graph PowerShell SDK nicht gefunden. Installiere es mit: " +
              "Install-Module Microsoft.Graph -Scope CurrentUser"
    }
    Import-Module Microsoft.Graph.Applications -ErrorAction Stop
    Import-Module Microsoft.Graph.Identity.SignIns -ErrorAction Stop
}

function Connect-GraphIfNeeded {
    $context = Get-MgContext
    if (-not $context) {
        Write-Host "Verbinde mit Microsoft Graph (interaktiver Login, Scopes: Application.ReadWrite.All, AppRoleAssignment.ReadWrite.All)..."
        Connect-MgGraph -Scopes "Application.ReadWrite.All", "AppRoleAssignment.ReadWrite.All" -NoWelcome
        $context = Get-MgContext
    }
    Write-Host "Verbunden mit Tenant: $($context.TenantId) als $($context.Account)"
    return $context
}

Assert-GraphModule
$context = Connect-GraphIfNeeded

Write-Host ""
Write-Host "=== Geplante Aenderungen in Entra Tenant $($context.TenantId) ===" -ForegroundColor Cyan
Write-Host "App Registration:  $DisplayName"
Write-Host "Application Permissions (Graph, admin-consent-pflichtig):"
foreach ($role in $RequiredGraphAppRoles) {
    Write-Host "  - $($role.Name)  ($($role.Reason))"
}
Write-Host ""

if (-not $Apply) {
    Write-Host "Dry-Run (kein -Apply uebergeben) - es wurde NICHTS in Entra ID angelegt." -ForegroundColor Yellow
    Write-Host "Fuehre mit -Apply aus, um die App Registration tatsaechlich zu erstellen."
    return
}

if (-not $PSCmdlet.ShouldProcess($context.TenantId, "App Registration '$DisplayName' + Graph Permissions anlegen")) {
    return
}

# Microsoft Graph Service Principal auflösen, um App Role IDs (nicht nur Namen) zu ermitteln.
$graphSp = Get-MgServicePrincipal -Filter "appId eq '$GraphResourceAppId'"
if (-not $graphSp) {
    throw "Microsoft Graph Service Principal nicht im Tenant gefunden - unerwarteter Zustand."
}

$resourceAccess = @()
foreach ($role in $RequiredGraphAppRoles) {
    $appRole = $graphSp.AppRoles | Where-Object { $_.Value -eq $role.Name -and $_.AllowedMemberTypes -contains "Application" }
    if (-not $appRole) {
        throw "Graph App Role '$($role.Name)' nicht gefunden. Pruefe den Rollennamen."
    }
    $resourceAccess += @{ Id = $appRole.Id; Type = "Role" }
}

Write-Host "Erstelle App Registration '$DisplayName'..."
$app = New-MgApplication -DisplayName $DisplayName -SignInAudience "AzureADMyOrg" -RequiredResourceAccess @(
    @{ ResourceAppId = $GraphResourceAppId; ResourceAccess = $resourceAccess }
)

Write-Host "Erstelle zugehoerigen Service Principal..."
$sp = New-MgServicePrincipal -AppId $app.AppId

Write-Host "Erstelle Client Secret (gueltig $SecretValidityMonths Monate)..."
$secretEnd = (Get-Date).AddMonths($SecretValidityMonths)
$passwordCred = Add-MgApplicationPassword -ApplicationId $app.Id -PasswordCredential @{
    displayName = $SecretDescription
    endDateTime = $secretEnd
}

Write-Host "Erteile Admin Consent fuer Application Permissions..."
foreach ($role in $RequiredGraphAppRoles) {
    $appRole = $graphSp.AppRoles | Where-Object { $_.Value -eq $role.Name -and $_.AllowedMemberTypes -contains "Application" }
    try {
        New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $sp.Id -PrincipalId $sp.Id `
            -ResourceId $graphSp.Id -AppRoleId $appRole.Id | Out-Null
        Write-Host "  Consent erteilt: $($role.Name)"
    }
    catch {
        Write-Warning "  Consent fuer $($role.Name) fehlgeschlagen - vermutlich fehlende Admin-Rechte der aktuellen Session. Erteile Consent manuell im Entra Admin Center (Enterprise Applications > $DisplayName > Permissions)."
    }
}

$result = [ordered]@{
    PLATFORM_TENANT_ID  = $context.TenantId
    DIRECTORY_TENANT_ID = $context.TenantId
    ENTRA_CLIENT_ID     = $app.AppId
    ENTRA_CLIENT_SECRET = $passwordCred.SecretText
    ENTRA_AUTHORITY     = "https://login.microsoftonline.com/$($context.TenantId)"
}

Write-Host ""
Write-Host "=== Ergebnis ===" -ForegroundColor Green
Write-Host "App Registration erstellt: AppId=$($app.AppId) ObjectId=$($app.Id)"
Write-Host "Client Secret läuft ab am: $secretEnd"
Write-Host ""
Write-Host "WICHTIG: Das Client Secret wird von Microsoft Graph nur einmalig im Klartext" -ForegroundColor Yellow
Write-Host "zurueckgegeben. Sichere es jetzt (z.B. per -WriteEnvLocal oder manuell in einen" -ForegroundColor Yellow
Write-Host "Key Vault, siehe scripts/sync-keyvault.ps1)." -ForegroundColor Yellow
Write-Host ""

if ($WriteEnvLocal) {
    $envLocalPath = Join-Path (Split-Path $PSScriptRoot -Parent) ".env.local"
    $lines = @()
    if (Test-Path $envLocalPath) {
        $lines = Get-Content $envLocalPath
    }
    else {
        $examplePath = Join-Path (Split-Path $PSScriptRoot -Parent) ".env.example"
        if (Test-Path $examplePath) {
            $lines = Get-Content $examplePath
        }
    }

    foreach ($key in $result.Keys) {
        $newLine = "$key=$($result[$key])"
        $existingIndex = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "^$key=") { $existingIndex = $i; break }
        }
        if ($existingIndex -ge 0) {
            $lines[$existingIndex] = $newLine
        }
        else {
            $lines += $newLine
        }
    }

    Set-Content -Path $envLocalPath -Value $lines
    Write-Host "Werte in $envLocalPath geschrieben (nicht committed, siehe .gitignore)." -ForegroundColor Green
}
else {
    Write-Host "Werte (manuell in .env.local uebernehmen, NICHT committen):"
    foreach ($key in $result.Keys) {
        if ($key -eq "ENTRA_CLIENT_SECRET") {
            Write-Host "  $key=********  (siehe Konsolen-Scrollback bzw. -WriteEnvLocal verwenden)"
        }
        else {
            Write-Host "  $key=$($result[$key])"
        }
    }
}

Write-Host ""
Write-Host "Naechster Schritt: setze B2B_MODE=DEV_INTEGRATION, ALLOW_GRAPH_WRITES=true und" -ForegroundColor Cyan
Write-Host "DIRECTORY_PROVIDER=graph / EMAIL_PROVIDER=graph nur in einer bewusst separaten" -ForegroundColor Cyan
Write-Host "Konfiguration - niemals als Default fuer LOCAL_MOCK." -ForegroundColor Cyan
