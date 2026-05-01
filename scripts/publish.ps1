<#
.SYNOPSIS
    Publishes EqApoTray as a single self-contained Windows executable.

.DESCRIPTION
    Produces a single .exe that does not require .NET runtime on the user's machine.
    Output: dist/EqApoTray-win-x64/EqApoTray.exe

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Runtime
    Target RID (default: win-x64). Use win-arm64 for ARM64.
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

Write-Host "Publishing $project -> $output" -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $output "EqApoTray.exe"
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "OK: $exe ($size MB)" -ForegroundColor Green
}
