# test-hub-add.ps1 -- End-to-end test: install a firmlet from the registry,
# then register it with the hub and start the WASM host.
#
# Prerequisites:
#   - `tw` CLI is built and on PATH
#   - Proxy and AssetRegistry runlets are running
#   - Firmlet registry is accessible (set TW_FIRMLET_REGISTRY_URL)
#
# Usage:
#   .\scripts\test-hub-add.ps1 -FirmletName myFirmlet [-Version 1.0.0]

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$FirmletName,

    [string]$Version,

    [string]$FirmletBaseDir = $env:TW_FIRMLET_BASE_DIR
)

$ErrorActionPreference = 'Stop'

if (-not $FirmletBaseDir) {
    $FirmletBaseDir = Join-Path $env:LOCALAPPDATA 'Tinkwell' 'Firmlets'
}

$AssetId = (& tw id).Trim()
$InstallDir = Join-Path $FirmletBaseDir $AssetId

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Write-Host "==> Asset ID:    $AssetId"
Write-Host "==> Firmlet:     $FirmletName@$($Version ? $Version : 'latest')"
Write-Host "==> Install dir: $InstallDir"
Write-Host ""

# Step 1: Download and install the firmlet package
Write-Host "==> Step 1: Installing firmlet from registry..."

$downloadArgs = @($FirmletName, '-o', (Join-Path $InstallDir 'package.zip'))
if ($Version) {
    $downloadArgs += @('--version', $Version)
}

& tw firmlet-registry download @downloadArgs
if ($LASTEXITCODE -ne 0) { throw "Firmlet download failed" }

Expand-Archive -Path (Join-Path $InstallDir 'package.zip') -DestinationPath $InstallDir -Force
Remove-Item (Join-Path $InstallDir 'package.zip') -Force -ErrorAction SilentlyContinue

Write-Host "==> Firmlet installed to $InstallDir"
Write-Host ""

# Step 2: Register with hub and start host
Write-Host "==> Step 2: Adding asset to hub..."
& tw hub add $InstallDir --asset-id $AssetId
if ($LASTEXITCODE -ne 0) { throw "Hub add failed" }

Write-Host ""
Write-Host "==> Done! Asset $AssetId is running."
Write-Host ""
Write-Host "To remove:  tw hub remove $AssetId"
