<#
.SYNOPSIS
    Publishes EqApoTray as a Native AOT Windows executable.

.DESCRIPTION
    Native AOT publish: no .NET runtime required on the target machine. Output:
    dist/EqApoTray-win-x64/EqApoTray.exe (and supporting WindowsAppSDK runtime DLLs).

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Runtime
    Target RID (default: win-x64). Use win-arm64 for ARM64 (requires native ARM64 build host).
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/EqApoTray/EqApoTray.csproj"
$output  = Join-Path $root "dist/EqApoTray-$Runtime"

Write-Host "Publishing $project -> $output (AOT, $Configuration / $Runtime)" -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    -p:PublishAot=true `
    -p:WindowsAppSDKSelfContained=true `
    -p:WindowsPackageType=None `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $output "EqApoTray.exe"
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    $totalSize = [math]::Round((Get-ChildItem $output -File -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "OK: $exe ($size MB exe, $totalSize MB total)" -ForegroundColor Green
}
