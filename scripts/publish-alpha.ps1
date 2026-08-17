[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$ProjectFile = Join-Path $RepoRoot "src\BootCampPerformanceControl\BootCampPerformanceControl.csproj"
$PublishProfile = Join-Path $RepoRoot "src\BootCampPerformanceControl\Properties\PublishProfiles\win-x64-alpha.pubxml"
$OutputDir = Join-Path $RepoRoot "artifacts\BootCampPerformanceControl-0.1.0-alpha-win-x64"

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}

if (-not (Test-Path -LiteralPath $PublishProfile)) {
    throw "Publish profile not found: $PublishProfile"
}

Write-Host "=== BootCamp Performance Control 0.1.0-alpha Publish (win-x64) ===" -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
Write-Host "Project file:    $ProjectFile"
Write-Host "Publish profile: $PublishProfile"
Write-Host "Output path:     $OutputDir"

# Clean only the dedicated alpha publish directory if it already exists
if (Test-Path -LiteralPath $OutputDir) {
    Write-Host "Cleaning output directory: $OutputDir"
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$publishArgs = @(
    "publish",
    $ProjectFile,
    "/p:PublishProfile=$PublishProfile",
    "-o", $OutputDir
)

Write-Host "Executing: dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$ExecutablePath = Join-Path $OutputDir "BootCampPerformanceControl.exe"
if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "Published executable was not found at expected path: $ExecutablePath"
}

$FinalOutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
Write-Host "Publish completed successfully." -ForegroundColor Green
Write-Host "Published output directory: $FinalOutputDir" -ForegroundColor Green
Write-Host "Published executable:       $ExecutablePath" -ForegroundColor Green
