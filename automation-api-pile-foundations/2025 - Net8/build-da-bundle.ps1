[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("2025")]
    [string]$RevitVersion = "2025",

    [string]$RevitInstallDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

$automationRoot = (Resolve-Path $PSScriptRoot).Path
$projectPath = Join-Path $automationRoot "src/PileFoundationsDA/PileFoundationsDA.csproj"
$bundleRoot = Join-Path $automationRoot "PileFoundationsDA.bundle"
$bundleContentsPath = Join-Path $bundleRoot "Contents"
$bundleManifestPath = Join-Path $bundleRoot "PackageContents.xml"
$addinManifestPath = Join-Path $bundleContentsPath "PileFoundationsDA8.addin"
$filesRoot = Join-Path $automationRoot "files"
$zipPath = Join-Path $filesRoot "PileFoundationsDA.bundle.zip"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found at $projectPath"
}

if (-not (Test-Path $bundleManifestPath)) {
    throw "Bundle manifest not found at $bundleManifestPath"
}

if (-not (Test-Path $addinManifestPath)) {
    throw "Bundle addin manifest not found at $addinManifestPath"
}

Assert-Command -Name "dotnet"

$buildArgs = @(
    "build"
    $projectPath
    "-c"
    $Configuration
    "/p:RevitVersion=$RevitVersion"
)

if ($RevitInstallDir) {
    if (-not (Test-Path $RevitInstallDir)) {
        throw "RevitInstallDir does not exist: $RevitInstallDir"
    }

    $resolvedRevitInstallDir = (Resolve-Path $RevitInstallDir).Path
    $buildArgs += "/p:RevitInstallDir=$resolvedRevitInstallDir"
}

Write-Host "Building Design Automation add-in..." -ForegroundColor Cyan
Write-Host "  Project: $projectPath"
Write-Host "  Configuration: $Configuration"
Write-Host "  RevitVersion: $RevitVersion"
if ($RevitInstallDir) {
    Write-Host "  RevitInstallDir: $resolvedRevitInstallDir"
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$targetFramework = "net8.0"
$buildOutputPath = Join-Path $automationRoot "src/PileFoundationsDA/bin/$Configuration/$targetFramework"

if (-not (Test-Path $buildOutputPath)) {
    throw "Build output folder not found at $buildOutputPath"
}

New-Item -ItemType Directory -Path $bundleContentsPath -Force | Out-Null
New-Item -ItemType Directory -Path $filesRoot -Force | Out-Null

Get-ChildItem -Path $bundleContentsPath -File |
    Where-Object { $_.Name -ne "PileFoundationsDA8.addin" } |
    Remove-Item -Force

Get-ChildItem -Path $buildOutputPath | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $bundleContentsPath -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path $bundleRoot -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Build completed successfully." -ForegroundColor Green
Write-Host "Build output: $buildOutputPath"
Write-Host "Bundle folder: $bundleRoot"
Write-Host "Bundle zip: $zipPath"
