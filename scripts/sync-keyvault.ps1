<#
.SYNOPSIS
    Spiegelt sicherheitsrelevante Werte aus .env.local in einen Azure Key Vault.

.DESCRIPTION
    Dieses Skript liest .env.local (siehe .env.example fuer den Ursprung/die Bedeutung
    jedes Werts) und schreibt die als "geheim" markierten Keys als Key-Vault-Secrets.
    Es erstellt NIEMALS selbst einen Key Vault - das ist Aufgabe von
    infra/modules/key-vault.bicep (nur wenn deployKeyVault=true deployed, Default false).

    Dieses Skript wird NICHT automatisch ausgefuehrt. In der lokalen Entwicklung
    (LOCAL_MOCK) ist eine Key-Vault-Anbindung nicht erforderlich - .env.local reicht.
    Erst wenn DEV_INTEGRATION/AZURE_DEV mit einem echten Key Vault betrieben wird, ist
    dieser Sync sinnvoll (README "Konfiguration erfolgt ueber .env.local ... bzw. wird
    in eine Key Vault gespiegelt").

.PARAMETER VaultName
    Name des Ziel-Key-Vaults (siehe Output "keyVaultUri" eines main.bicep-Deployments
    mit deployKeyVault=true).

.PARAMETER EnvFile
    Pfad zur lokalen Env-Datei. Default: .env.local im Repository-Root.

.PARAMETER WhatIf
    Zeigt nur an, welche Secrets geschrieben wuerden (Standardverhalten von
    -WhatIf via SupportsShouldProcess) - siehe -Apply fuer den echten Schreibvorgang.

.PARAMETER Apply
    Schreibt die Secrets tatsaechlich in den Key Vault. Ohne diesen Switch: Dry-Run.

.EXAMPLE
    ./scripts/sync-keyvault.ps1 -VaultName b2bdev-kv
    # Dry-Run: zeigt an, welche Keys aus .env.local gespiegelt wuerden.

.EXAMPLE
    ./scripts/sync-keyvault.ps1 -VaultName b2bdev-kv -Apply
    # Schreibt die Secret-Keys tatsaechlich in den Key Vault (erfordert az login
    # und "Key Vault Secrets Officer" auf dem Vault, siehe key-vault.bicep).

.NOTES
    Nur die folgenden Keys gelten als geheim/schreibenswert - reine Konfigurationswerte
    (z.B. B2B_MODE, Provider-Auswahl) bleiben bewusst in .env.local/App-Konfiguration und
    werden nicht gespiegelt, um den Vault nicht mit Nicht-Secrets zu ueberladen.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$VaultName,

    [string]$EnvFile = (Join-Path (Split-Path $PSScriptRoot -Parent) ".env.local"),

    [switch]$Apply
)

$ErrorActionPreference = "Stop"

# Nur echte Secrets spiegeln - Provider-Flags/URLs bleiben App-Konfiguration.
$SecretKeys = @(
    "ENTRA_CLIENT_SECRET",
    "ENTRA_CLIENT_ID",
    "PLATFORM_TENANT_ID",
    "DIRECTORY_TENANT_ID",
    "ENTRA_AUTHORITY",
    "NOTIFICATIONS_SHARED_MAILBOX"
)

if (-not (Test-Path $EnvFile)) {
    throw "$EnvFile nicht gefunden. Lege sie zuerst an (Kopie von .env.example, " +
          "z.B. via scripts/setup-entra-app.ps1 -WriteEnvLocal)."
}

if (-not (Get-Module -ListAvailable -Name Az.KeyVault)) {
    throw "Az.KeyVault PowerShell-Modul nicht gefunden. Installiere es mit: " +
          "Install-Module Az.KeyVault -Scope CurrentUser"
}
Import-Module Az.KeyVault -ErrorAction Stop

$envValues = @{}
foreach ($line in Get-Content $EnvFile) {
    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
    $key, $value = $line -split '=', 2
    $envValues[$key.Trim()] = $value.Trim()
}

Write-Host "=== Geplanter Key-Vault-Sync: $VaultName ===" -ForegroundColor Cyan
$toSync = @{}
foreach ($key in $SecretKeys) {
    if ($envValues.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($envValues[$key])) {
        $toSync[$key] = $envValues[$key]
        $masked = if ($key -like "*SECRET*") { "********" } else { $envValues[$key] }
        Write-Host "  $key -> $masked"
    }
}

if ($toSync.Count -eq 0) {
    Write-Host "Keine Secret-Werte in $EnvFile gesetzt - nichts zu tun." -ForegroundColor Yellow
    return
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry-Run (kein -Apply uebergeben) - es wurde NICHTS in den Key Vault geschrieben." -ForegroundColor Yellow
    Write-Host "Fuehre mit -Apply aus, um die Secrets tatsaechlich zu schreiben."
    return
}

if (-not $PSCmdlet.ShouldProcess($VaultName, "Secrets aus $EnvFile schreiben")) {
    return
}

foreach ($key in $toSync.Keys) {
    $secretName = $key.ToLower().Replace('_', '-')
    $secureValue = ConvertTo-SecureString $toSync[$key] -AsPlainText -Force
    Set-AzKeyVaultSecret -VaultName $VaultName -Name $secretName -SecretValue $secureValue | Out-Null
    Write-Host "Secret '$secretName' geschrieben." -ForegroundColor Green
}

Write-Host ""
Write-Host "Fertig. $($toSync.Count) Secret(s) in Key Vault '$VaultName' gespiegelt." -ForegroundColor Green
