[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("2025", "2026")]
    [string]$RevitVersion = "2025",

    [ValidateSet("CurrentUser", "AllUsers")]
    [string]$Scope = "CurrentUser",

    [string]$AssemblyPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$manifestTemplatePath = Join-Path $repoRoot "src/PadFoundationImport/Manifest/PadFoundationImport.addin"

if (-not (Test-Path $manifestTemplatePath)) {
    throw "Manifest template not found at $manifestTemplatePath"
}

if (-not $AssemblyPath) {
    $targetFramework = "net8.0-windows"
    $AssemblyPath = Join-Path $repoRoot "src/PadFoundationImport/bin/$Configuration/$targetFramework/PadFoundationImport.dll"
}

if (-not (Test-Path $AssemblyPath)) {
    throw "Built add-in assembly not found at $AssemblyPath. Build the add-in first."
}

$addinRoot = switch ($Scope) {
    "CurrentUser" { Join-Path $env:APPDATA "Autodesk/Revit/Addins/$RevitVersion" }
    "AllUsers" { Join-Path $env:ProgramData "Autodesk/Revit/Addins/$RevitVersion" }
}

New-Item -ItemType Directory -Path $addinRoot -Force | Out-Null

$manifestOutputPath = Join-Path $addinRoot "PadFoundationImport.addin"
$assemblyPathEscaped = [System.Security.SecurityElement]::Escape((Resolve-Path $AssemblyPath).Path)
$manifestContent = Get-Content -Raw -Path $manifestTemplatePath
$manifestContent = [System.Text.RegularExpressions.Regex]::Replace(
    $manifestContent,
    "<Assembly>.*?</Assembly>",
    "<Assembly>$assemblyPathEscaped</Assembly>"
)

Set-Content -Path $manifestOutputPath -Value $manifestContent -Encoding utf8

Write-Host "Registered Revit add-in manifest." -ForegroundColor Green
Write-Host "  Scope: $Scope"
Write-Host "  RevitVersion: $RevitVersion"
Write-Host "  Manifest: $manifestOutputPath"
Write-Host "  Assembly: $AssemblyPath"
