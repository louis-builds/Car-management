# Deploys frontend/index.html to the Azure Storage static website.
# Run from anywhere: .\deploy.ps1  (or right-click > Run with PowerShell)

$ErrorActionPreference = "Stop"

$storageAccountName = "carbatteryrg9837"
$indexFile = Join-Path $PSScriptRoot "index.html"

if (-not (Get-AzContext)) {
    Write-Host "Not logged in to Azure - opening login..."
    Connect-AzAccount | Out-Null
}

Write-Host "Uploading $indexFile to $storageAccountName/`$web/index.html ..."
$ctx = New-AzStorageContext -StorageAccountName $storageAccountName -UseConnectedAccount
Set-AzStorageBlobContent -File $indexFile -Container '$web' -Blob "index.html" `
    -Context $ctx -Properties @{ContentType = "text/html" } -Force | Out-Null

Write-Host "Done: https://$storageAccountName.z44.web.core.windows.net/"
