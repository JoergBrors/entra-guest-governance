param(
    [string]$Endpoint = "https://cosmos:8081",
    [string]$Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
    [string]$DatabaseId = "b2b-governance-dev"
)

$ErrorActionPreference = "Stop"

function Invoke-CosmosRequest {
    param(
        [string]$Method,
        [string]$ResourceType,
        [string]$ResourceLink,
        [string]$Path,
        [hashtable]$Body = $null,
        [string]$PartitionKey = $null
    )

    $utcDate = [DateTime]::UtcNow.ToString("r")
    $stringToSign = "$($Method.ToLowerInvariant())`n$($ResourceType.ToLowerInvariant())`n$ResourceLink`n$($utcDate.ToLowerInvariant())`n`n"
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Convert]::FromBase64String($Key))
    $signature = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stringToSign)))
    $authEncoded = [Uri]::EscapeDataString("type=master&ver=1.0&sig=$signature")

    $headers = @{
        "x-ms-date" = $utcDate
        "x-ms-version" = "2018-12-31"
        "Authorization" = $authEncoded
    }
    if ($PartitionKey) {
        $headers["x-ms-documentdb-partitionkey"] = "[`"$PartitionKey`"]"
    }

    $params = @{
        Method = $Method
        Uri = "$Endpoint/$Path"
        Headers = $headers
        ContentType = "application/json"
        SkipCertificateCheck = $true
        ErrorAction = "Stop"
    }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    Invoke-RestMethod @params
}

for ($i = 1; $i -le 60; $i++) {
    try {
        Invoke-CosmosRequest -Method "GET" -ResourceType "dbs" -ResourceLink "" -Path "dbs" | Out-Null
        break
    }
    catch {
        if ($i -eq 60) { throw }
        Start-Sleep -Seconds 5
    }
}

try {
    Invoke-CosmosRequest -Method "POST" -ResourceType "dbs" -ResourceLink "" -Path "dbs" -Body @{ id = $DatabaseId } | Out-Null
    Write-Host "Database '$DatabaseId' created."
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
    Write-Host "Database '$DatabaseId' already exists."
}

$containers = @(
    @{ id = "domain"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "discovery"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "entraid"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "jobs"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" }; defaultTtl = -1 },
    @{ id = "audit"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" }; defaultTtl = -1 }
)

foreach ($container in $containers) {
    try {
        Invoke-CosmosRequest -Method "POST" -ResourceType "colls" -ResourceLink "dbs/$DatabaseId" -Path "dbs/$DatabaseId/colls" -Body $container | Out-Null
        Write-Host "Container '$($container.id)' created."
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
        Write-Host "Container '$($container.id)' already exists."
    }
}

# Henne-Ei-Problem (siehe scripts/reset-cosmos-dev-data.ps1, dieselbe Logik): ohne diesen
# Seed-Datensatz gaebe es nach einem frischen Compose-Up keinen Weg, sich ueber
# POST /api/auth/mock/login anzumelden, da MockEntraDirectoryStore beim API-Start aus dem
# Container "entraid" hydriert (CosmosMockEntraUserRepository, entityType "MockEntraUser").
# Feldnamen/Casing muessen exakt zu MockEntraUserDocument passen (camelCase JsonPropertyName).
$adminTenantId = "dev-tenant-a"
$adminMail = "admin@platform.example"
$adminObjectId = "mock-member-admin"
$adminUserDoc = @{
    id                = "mock-entra-user-$adminObjectId"
    entityType        = "MockEntraUser"
    platformTenantId  = $adminTenantId
    objectId          = $adminObjectId
    userPrincipalName = $adminMail
    mail              = $adminMail
    displayName       = "Platform Admin"
    givenName         = "Platform"
    surname           = "Admin"
    companyName       = "Platform"
    department        = "IT"
    jobTitle          = "Governance Administrator"
    sponsor           = "configuration required"
    accountEnabled    = "true"
    userType          = "Member"
    portalRoles       = @("GovernanceAdmin", "User", "Reviewer")
    lastLoginAt       = $null
}

try {
    Invoke-CosmosRequest -Method "POST" -ResourceType "docs" -ResourceLink "dbs/$DatabaseId/colls/entraid" -Path "dbs/$DatabaseId/colls/entraid/docs" -Body $adminUserDoc -PartitionKey $adminTenantId | Out-Null
    Write-Host "Mock-Entra-Benutzer '$adminMail' (Rolle GovernanceAdmin, Tenant '$adminTenantId') created."
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
    Write-Host "Mock-Entra-Benutzer '$adminMail' already exists."
}

# Standard-Mock-Gruppen (dieselbe Logik wie scripts/reset-cosmos-dev-data.ps1, siehe dortiger
# Kommentar): seit CosmosMockEntraDirectoryRepository sind Gruppen/Mitgliedschaften in Cosmos
# persistiert (entityType "MockEntraGroup"/"MockEntraMembership"), platformTenantId ist dabei
# immer der feste Platzhalterwert "mock-entra".
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
    try {
        Invoke-CosmosRequest -Method "POST" -ResourceType "docs" -ResourceLink "dbs/$DatabaseId/colls/entraid" -Path "dbs/$DatabaseId/colls/entraid/docs" -Body $groupDoc -PartitionKey $mockPartition | Out-Null
        Write-Host "Mock-Entra-Gruppe '$($groupDoc.displayName)' created."
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
        Write-Host "Mock-Entra-Gruppe '$($groupDoc.displayName)' already exists."
    }
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
    try {
        Invoke-CosmosRequest -Method "POST" -ResourceType "docs" -ResourceLink "dbs/$DatabaseId/colls/entraid" -Path "dbs/$DatabaseId/colls/entraid/docs" -Body $membershipDoc -PartitionKey $mockPartition | Out-Null
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
    }
}
Write-Host "$($membershipDocs.Count) Mock-Entra-Mitgliedschaft(en) created (or already existed)."
