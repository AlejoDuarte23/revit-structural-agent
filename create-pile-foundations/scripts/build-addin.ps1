[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("2025", "2026")]
    [string]$RevitVersion = "2025",

    [string]$RevitInstallDir,

    [switch]$RegisterAddin,

    [ValidateSet("CurrentUser", "AllUsers")]
    [string]$AddinScope = "CurrentUser"
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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src/PileFoundationImport/PileFoundationImport.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found at $projectPath"
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

Write-Host "Building Revit add-in..." -ForegroundColor Cyan
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

$targetFramework = "net8.0-windows"
$assemblyPath = Join-Path $repoRoot "src/PileFoundationImport/bin/$Configuration/$targetFramework/PileFoundationImport.dll"

if ($RegisterAddin) {
    $registerScript = Join-Path $PSScriptRoot "register-addin.ps1"
    $registerArgs = @(
        "-Configuration", $Configuration,
        "-RevitVersion", $RevitVersion,
        "-Scope", $AddinScope,
        "-AssemblyPath", $assemblyPath
    )

    & $registerScript @registerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Add-in registration failed with exit code $LASTEXITCODE"
    }
}

Write-Host ""
Write-Host "Build completed successfully." -ForegroundColor Green
Write-Host "Add-in DLL output: $(Split-Path -Parent $assemblyPath)"
Write-Host "Add-in DLL: $assemblyPath"
