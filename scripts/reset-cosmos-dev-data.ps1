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

$containers = @(
    @{ id = "domain"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
    @{ id = "discovery"; partitionKey = @{ paths = @("/platformTenantId"); kind = "Hash" } },
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
