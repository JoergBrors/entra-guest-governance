<#
.SYNOPSIS
    Loescht die lokale Cosmos-Emulator-Dev-Datenbank und legt die Portal-Container neu an.
#>
param(
    [string]$Endpoint = "https://localhost:8081",
    [string]$Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
    [string]$DatabaseId = "b2b-governance-dev"
)

$ErrorActionPreference = "Stop"

function Invoke-CosmosEmulatorRequest {
    param(
        [string]$Method,
        [string]$ResourceType,
        [string]$ResourceLink,
        [string]$Path,
        [hashtable]$Body = $null
    )

    $utcDate = [DateTime]::UtcNow.ToString("r")
    $stringToSign = "$($Method.ToLowerInvariant())`n$($ResourceType.ToLowerInvariant())`n$ResourceLink`n$($utcDate.ToLowerInvariant())`n`n"
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Convert]::FromBase64String($Key))
    $signature = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stringToSign)))
    $authEncoded = [Uri]::EscapeDataString("type=master&ver=1.0&sig=$signature")

    $params = @{
        Method = $Method
        Uri = "$Endpoint/$Path"
        Headers = @{
            "x-ms-date" = $utcDate
            "x-ms-version" = "2018-12-31"
            "Authorization" = $authEncoded
        }
        ContentType = "application/json"
        SkipCertificateCheck = $true
        ErrorAction = "Stop"
    }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    Invoke-RestMethod @params
}

# Fuenf Container gemaess infra/modules/cosmos-free-tier.bicep — "entraid" ist der
# dedizierte Mock-Entra-Verzeichnis-Container (Erweiterung 2026-08-31 "EntraId-Persistenz"),
# getrennt vom "discovery"-Container (Actual State/ResourceAccess), weil es sich fachlich um
# den (gemockten) Verzeichnis-Bestand selbst handelt, nicht um dagegen abgeglichene Funde.
$containers = @(
    @{ id = "domain"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "discovery"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "entraid"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "jobs"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" }; defaultTtl = -1 },
    @{ id = "audit"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" }; defaultTtl = -1 }
)

try {
    Invoke-CosmosEmulatorRequest -Method "DELETE" -ResourceType "dbs" -ResourceLink "dbs/$DatabaseId" -Path "dbs/$DatabaseId" | Out-Null
    Write-Host "Datenbank '$DatabaseId' geloescht." -ForegroundColor Yellow
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 404) {
        Write-Host "Datenbank '$DatabaseId' war nicht vorhanden." -ForegroundColor DarkYellow
    }
    else {
        throw
    }
}

Invoke-CosmosEmulatorRequest -Method "POST" -ResourceType "dbs" -ResourceLink "" -Path "dbs" -Body @{ id = $DatabaseId } | Out-Null
Write-Host "Datenbank '$DatabaseId' angelegt." -ForegroundColor Green

foreach ($container in $containers) {
    Invoke-CosmosEmulatorRequest -Method "POST" -ResourceType "colls" -ResourceLink "dbs/$DatabaseId" -Path "dbs/$DatabaseId/colls" -Body $container | Out-Null
    Write-Host "Container '$($container.id)' angelegt." -ForegroundColor Green
}

# Henne-Ei-Problem nach einem Reset (Erweiterung 2026-08-30 (Teil 3), seither umgezogen auf
# den eigenen Container "entraid", siehe docs/development/local-mock.md "Mock Entra
# Directory"): MockEntraDirectoryStore hydriert beim API-/Worker-Start aus dem Container
# "entraid" (IMockEntraUserRepository/IMockEntraDirectoryRepository, entityType
# "MockEntraUser"/"MockEntraGroup"/"MockEntraMembership"). Ohne diesen Seed-Datensatz gaebe es
# nach einem Reset keinen Weg, sich ueber POST /api/auth/mock/login anzumelden, um ueberhaupt
# erst weitere Mock-User/Gaeste anzulegen. Feldnamen/Casing muessen exakt zu
# CosmosMockEntraUserRepository.MockEntraUserDocument passen (camelCase JsonPropertyName).
$adminTenantId = "dev-tenant-a"
$adminMail = "admin@platform.example"
$adminObjectId = "mock-member-admin"
$adminUserDoc = @{
    id                 = "mock-entra-user-$adminObjectId"
    entityType         = "MockEntraUser"
    platformTenantId   = $adminTenantId
    objectId           = $adminObjectId
    userPrincipalName  = $adminMail
    mail               = $adminMail
    displayName        = "Platform Admin"
    givenName          = "Platform"
    surname            = "Admin"
    companyName        = "Platform"
    department         = "IT"
    jobTitle           = "Governance Administrator"
    sponsor            = "configuration required"
    accountEnabled     = "true"
    userType           = "Member"
    portalRoles        = @("GovernanceAdmin", "User", "Reviewer")
    lastLoginAt         = $null
}

function New-EntraIdDoc {
    param([hashtable]$Doc, [string]$PartitionKeyValue)
    $utcDate = [DateTime]::UtcNow.ToString("r")
    $stringToSign = "post`ndocs`ndbs/$DatabaseId/colls/entraid`n$($utcDate.ToLowerInvariant())`n`n"
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Convert]::FromBase64String($Key))
    $signature = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stringToSign)))
    $authEncoded = [Uri]::EscapeDataString("type=master&ver=1.0&sig=$signature")

    Invoke-RestMethod -Method Post `
        -Uri "$Endpoint/dbs/$DatabaseId/colls/entraid/docs" `
        -Headers (@{
            "x-ms-date"                    = $utcDate
            "x-ms-version"                 = "2018-12-31"
            "Authorization"                = $authEncoded
            "x-ms-documentdb-partitionkey" = "[`"$PartitionKeyValue`"]"
        }) `
        -ContentType "application/json" `
        -Body ($Doc | ConvertTo-Json -Depth 10) `
        -SkipCertificateCheck `
        -ErrorAction Stop | Out-Null
}

New-EntraIdDoc -Doc $adminUserDoc -PartitionKeyValue $adminTenantId
Write-Host "Mock-Entra-Benutzer '$adminMail' (Rolle GovernanceAdmin, Tenant '$adminTenantId') im Container 'entraid' angelegt." -ForegroundColor Green

# Standard-Mock-Gruppen (Erweiterung 2026-08-31: seit CosmosMockEntraDirectoryRepository sind
# Gruppen/Mitgliedschaften genau wie Users in Cosmos persistiert, entityType "MockEntraGroup"/
# "MockEntraMembership" — ohne diesen Seed waeren nach einem Reset zwar Users, aber KEINE
# Gruppen vorhanden, bis jemand ueber die API eine anlegt. platformTenantId ist bei diesen
# Dokumenten immer der feste Platzhalterwert "mock-entra" (siehe dortiger Klassenkommentar in
# CosmosMockEntraDirectoryRepository.cs) — Gruppen sind im Mock-Entra-Stamm nicht
# tenant-gebunden, brauchen aber trotzdem ein platformTenantId-Feld, weil der Container
# "entraid" ueberall mit Partition-Key-Pfad "/platformTenantId" angelegt wird.
$mockPartition = "mock-entra"
$groupDocs = @(
    @{ id = "mock-entra-group-mock-grp-reader"; entityType = "MockEntraGroup"; platformTenantId = $mockPartition
       objectId = "mock-grp-reader"; displayName = "SG-DEMO-READER"; mailNickname = "sg-demo-reader"
       description = "Mock security group for reader access."; groupTypes = @(); mailEnabled = $false
       securityEnabled = $true; resourceProvisioningOptions = @() },
    @{ id = "mock-entra-group-mock-grp-contributor"; entityType = "MockEntraGroup"; platformTenantId = $mockPartition
       objectId = "mock-grp-contributor"; displayName = "SG-DEMO-CONTRIBUTOR"; mailNickname = "sg-demo-contributor"
       description = "Mock security group for contributor access."; groupTypes = @(); mailEnabled = $false
       securityEnabled = $true; resourceProvisioningOptions = @() },
    @{ id = "mock-entra-group-mock-m365-collab"; entityType = "MockEntraGroup"; platformTenantId = $mockPartition
       objectId = "mock-m365-collab"; displayName = "M365-DEMO-COLLAB"; mailNickname = "m365-demo-collab"
       description = "Mock Microsoft 365 collaboration group."; groupTypes = @("Unified"); mailEnabled = $true
       securityEnabled = $false; resourceProvisioningOptions = @() }
)
foreach ($groupDoc in $groupDocs) {
    New-EntraIdDoc -Doc $groupDoc -PartitionKeyValue $mockPartition
    Write-Host "Mock-Entra-Gruppe '$($groupDoc.displayName)' im Container 'entraid' angelegt." -ForegroundColor Green
}

$membershipDocs = @(
    @{ id = "mock-entra-membership-mock-grp-reader-mock-obj-anna"; entityType = "MockEntraMembership"
       platformTenantId = $mockPartition; groupId = "mock-grp-reader"; entraObjectId = "mock-obj-anna" },
    @{ id = "mock-entra-membership-mock-grp-reader-mock-obj-peter"; entityType = "MockEntraMembership"
       platformTenantId = $mockPartition; groupId = "mock-grp-reader"; entraObjectId = "mock-obj-peter" },
    @{ id = "mock-entra-membership-mock-grp-contributor-mock-obj-peter"; entityType = "MockEntraMembership"
       platformTenantId = $mockPartition; groupId = "mock-grp-contributor"; entraObjectId = "mock-obj-peter" },
    @{ id = "mock-entra-membership-mock-m365-collab-mock-obj-lea"; entityType = "MockEntraMembership"
       platformTenantId = $mockPartition; groupId = "mock-m365-collab"; entraObjectId = "mock-obj-lea" }
)
foreach ($membershipDoc in $membershipDocs) {
    New-EntraIdDoc -Doc $membershipDoc -PartitionKeyValue $mockPartition
}
Write-Host "$($membershipDocs.Count) Mock-Entra-Mitgliedschaft(en) im Container 'entraid' angelegt." -ForegroundColor Green

Write-Host ""
Write-Host "WICHTIG: Die Portal API muss jetzt (neu) gestartet werden, damit sie diese Daten" -ForegroundColor Cyan
Write-Host "beim Start in den Mock-Entra-Store hydriert (siehe Program.cs Startup-Hydration)." -ForegroundColor Cyan
Write-Host "Ablauf: ./scripts/reset-cosmos-dev-data.ps1  ->  API (neu) starten  ->  ./scripts/seed-dev-data.ps1" -ForegroundColor Cyan
