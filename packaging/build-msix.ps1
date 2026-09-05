<#
.SYNOPSIS
    Builds an MSIX package for HeyAI.

.DESCRIPTION
    A script rather than a .wapproj on purpose. A Windows Application Packaging Project
    pulls in MSBuild targets that only Visual Studio installs reliably, and the whole job
    here is three steps: publish, stage, pack. This runs anywhere the Windows SDK is.

    The output is unsigned. That is deliberate:

      * for local testing, dev mode registers a loose layout with no certificate at all,
        which is what -Register does and what CI could do
      * for release, the Microsoft Store signs the package during submission

    Signing locally would mean creating a certificate and adding it to a trust store,
    which is a machine-wide security change nobody should make casually. If you need a
    sideloadable signed package, sign the output yourself with signtool and a certificate
    you already trust.

.PARAMETER Version
    Four-part version stamped into the manifest. MSIX requires the fourth part to be 0
    for Store submission.

.PARAMETER Register
    Skip packing and register the staged layout in place. Needs Developer Mode. This is
    the fast loop: no certificate, no install, and `heyai.exe` lands on PATH with package
    identity so you can verify with `heyai doctor`.

.EXAMPLE
    ./packaging/build-msix.ps1 -Register
    heyai doctor        # should report: identity : packaged (...)

.EXAMPLE
    ./packaging/build-msix.ps1 -Version 0.2.0.0
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0.0',

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [switch]$Register
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $repo 'artifacts/msix'
$output = Join-Path $repo "artifacts/HeyAI-$Version-$Architecture.msix"

Write-Host "Publishing $Architecture..." -ForegroundColor Cyan

# Self-contained: an installed package cannot assume a .NET runtime is present, and a
# missing-runtime failure at activation surfaces as an unexplained exit code rather than
# a message anyone can act on.
& dotnet publish (Join-Path $repo 'src/HeyAI.Server') `
    --configuration Release `
    --runtime "win-$Architecture" `
    --self-contained true `
    --output $staging `
    /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host 'Staging package files...' -ForegroundColor Cyan

Copy-Item (Join-Path $PSScriptRoot 'Assets') $staging -Recurse -Force

# The manifest is templated rather than edited in place, so a build never leaves the
# working tree dirty.
$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
$manifest = $manifest -replace 'Version="0\.1\.0\.0"', "Version=`"$Version`""
$manifest = $manifest -replace 'ProcessorArchitecture="x64"', "ProcessorArchitecture=`"$Architecture`""
Set-Content (Join-Path $staging 'AppxManifest.xml') $manifest -Encoding utf8

# Nothing here is loaded by the package, and PDBs would roughly double its size.
Get-ChildItem $staging -Filter *.pdb -Recurse | Remove-Item -Force

if ($Register) {
    Write-Host 'Registering the staged layout (Developer Mode)...' -ForegroundColor Cyan
    Add-AppxPackage -Register (Join-Path $staging 'AppxManifest.xml')
    Write-Host 'Registered. Verify with: heyai doctor' -ForegroundColor Green
    return
}

$makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $makeappx) {
    throw 'makeappx.exe not found. Install the Windows 10/11 SDK.'
}

Write-Host "Packing with $($makeappx.FullName)..." -ForegroundColor Cyan
& $makeappx.FullName pack /d $staging /p $output /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

Write-Host "Built $output" -ForegroundColor Green
Write-Host 'Unsigned. Sign it yourself for sideloading, or submit it to the Store.' -ForegroundColor Yellow
