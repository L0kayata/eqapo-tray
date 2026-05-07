<#
.SYNOPSIS
    Publishes EqApoTray (Native AOT), strips unused WindowsAppSDK
    components, packages a release zip, and writes SHA256SUMS.txt.

.DESCRIPTION
    Pipeline:
      1. dotnet publish (Native AOT, self-contained) → dist/EqApoTray-win-x64/
      2. Strip components this app provably does not load:
           - EqApoTray.pdb
           - AI/ML runtime (onnxruntime, DirectML, Microsoft.Windows.AI.*,
             Imaging, Workloads, Vision, NpuDetect/)
           - Microsoft.Windows.Widgets, Microsoft.Web.WebView2,
             WebView2Loader, WinUIEdit
           - Locale *.mui folders other than zh-CN (NeutralLanguage) and
             en-us (fallback)
         Anything that touches XAML, composition, MRT, or the
         WindowsAppRuntime bootstrap stays in.
      3. Compress dist/EqApoTray-win-x64/ → dist/EqApoTray-v<Version>-win-x64.zip
      4. Hash the zip → dist/SHA256SUMS.txt

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Runtime
    Target RID (default: win-x64). arm64 needs a native arm64 build host.

.PARAMETER Version
    Override version string. Defaults to <Version> from EqApoTray.csproj.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime       = "win-x64",
    [string]$Version
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/EqApoTray/EqApoTray.csproj"
$output  = Join-Path $root "dist/EqApoTray-$Runtime"

if (-not $Version) {
    [xml]$csproj = Get-Content $project
    $Version = ($csproj.Project.PropertyGroup.Version |
                Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) { $Version = "0.0.0" }
}

Write-Host "Publishing $project -> $output (AOT, $Configuration / $Runtime, v$Version)" -ForegroundColor Cyan

if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}

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

# ---------- Strip unused components ----------

Write-Host "Stripping unused components..." -ForegroundColor Cyan

$stripFilePatterns = @(
    # Debug symbols
    'EqApoTray.pdb',
    # AI / ML runtime
    'onnxruntime.dll',
    'onnxruntime_providers_shared.dll',
    'DirectML.dll',
    'Microsoft.Windows.AI*.dll',
    'Microsoft.Windows.AI*.winmd',
    'Microsoft.Graphics.Imaging.dll',
    'Microsoft.Graphics.Imaging.winmd',
    'Microsoft.Graphics.ImagingInternal*.winmd',
    'Microsoft.Graphics.Internal.Imaging.winmd',
    'Microsoft.Windows.Workloads.dll',
    'Microsoft.Windows.Workloads.Resources*.dll',
    'Microsoft.Windows.Workloads.winmd',
    'Microsoft.Windows.Private.Workloads.SessionManager.winmd',
    'Microsoft.Windows.SemanticSearch.winmd',
    'Microsoft.Windows.Vision.winmd',
    'Microsoft.Windows.VisionInternal.winmd',
    'Microsoft.Windows.Internal.Vision.winmd',
    'workloads.json',
    'workloads.*.json',
    # Widgets
    'Microsoft.Windows.Widgets.dll',
    'Microsoft.Windows.Widgets.winmd',
    # WebView2 (this app embeds no web content)
    'Microsoft.Web.WebView2.Core.dll',
    'WebView2Loader.dll'
    # Do NOT strip WinUIEdit.dll here. The app has no RichEditBox, so it
    # *looks* safe to remove, but WinUI 3 crashes at startup without it
    # (verified empirically on WindowsAppSDK 1.8). Leave it bundled.
)

$stripDirs = @('NpuDetect')

# Locale folders to keep. Everything else matching a locale-id pattern
# (lowercase-leading hyphenated id like 'zh-CN', 'gd-gb', 'sr-Latn-RS')
# gets removed. NeutralLanguage in the csproj is zh-CN, en-us is the
# WinUI fallback chain root.
$keepLocales = @('zh-CN', 'en-us')

$stripBytes = 0L
$stripCount = 0

foreach ($pattern in $stripFilePatterns) {
    Get-ChildItem -Path $output -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
        $stripBytes += $_.Length
        $stripCount++
        Remove-Item $_.FullName -Force
    }
}

foreach ($dir in $stripDirs) {
    $dirPath = Join-Path $output $dir
    if (Test-Path $dirPath) {
        Get-ChildItem $dirPath -Recurse -File | ForEach-Object {
            $stripBytes += $_.Length
            $stripCount++
        }
        Remove-Item $dirPath -Recurse -Force
    }
}

Get-ChildItem -Path $output -Directory | Where-Object {
    $_.Name -match '^[a-z]+(-[A-Za-z]+)+$' -and ($_.Name -notin $keepLocales)
} | ForEach-Object {
    $localeDir = $_
    Get-ChildItem $localeDir.FullName -Recurse -File | ForEach-Object {
        $stripBytes += $_.Length
        $stripCount++
    }
    Remove-Item $localeDir.FullName -Recurse -Force
}

Write-Host ("Stripped {0} files / {1:N1} MB" -f $stripCount, ($stripBytes / 1MB)) -ForegroundColor Yellow

# ---------- Sanity check + size summary ----------

$exe = Join-Path $output "EqApoTray.exe"
if (-not (Test-Path $exe)) { throw "Publish output missing $exe" }

$exeSize   = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$totalSize = [math]::Round((Get-ChildItem $output -File -Recurse | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "OK: $exe ($exeSize MB exe, $totalSize MB total)" -ForegroundColor Green

# ---------- Zip ----------

$zipName = "EqApoTray-v$Version-$Runtime.zip"
$zipPath = Join-Path $root "dist/$zipName"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Write-Host "Packing $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "OK: $zipPath ($zipSize MB)" -ForegroundColor Green

# ---------- SHA256SUMS.txt ----------

$sumPath = Join-Path $root "dist/SHA256SUMS.txt"
$sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
"$sha  $zipName" | Out-File $sumPath -Encoding ASCII
Write-Host "OK: $sumPath" -ForegroundColor Green
Write-Host "  $sha  $zipName" -ForegroundColor Gray
