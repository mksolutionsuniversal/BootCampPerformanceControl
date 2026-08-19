[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$ProjectFile = Join-Path $RepoRoot "src\BootCampPerformanceControl\BootCampPerformanceControl.csproj"
$PublishProfile = Join-Path $RepoRoot "src\BootCampPerformanceControl\Properties\PublishProfiles\win-x64-release.pubxml"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}

[xml]$ProjectXml = Get-Content -LiteralPath $ProjectFile -Raw
$VersionNode = $ProjectXml.SelectSingleNode("/Project/PropertyGroup/Version")
if ($null -eq $VersionNode) {
    throw "Version property not found in project file: $ProjectFile"
}

$Version = [string]$VersionNode.InnerText
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version property is empty in project file: $ProjectFile"
}

$Version = $Version.Trim()
$OutputDir = Join-Path $ArtifactsDir "BootCampPerformanceControl-$Version-win-x64"
$ZipPath = Join-Path $ArtifactsDir "BootCampPerformanceControl-$Version-win-x64.zip"

if (-not (Test-Path -LiteralPath $PublishProfile)) {
    throw "Publish profile not found: $PublishProfile"
}

Write-Host "=== BootCamp Performance Control $Version Release Publish (win-x64) ===" -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
Write-Host "Project file:    $ProjectFile"
Write-Host "Publish profile: $PublishProfile"
Write-Host "Version:         $Version"
Write-Host "Output path:     $OutputDir"
Write-Host "ZIP path:        $ZipPath"

# Clean only the dedicated versioned publish directory and ZIP if they already exist
if (Test-Path -LiteralPath $OutputDir) {
    Write-Host "Cleaning output directory: $OutputDir"
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

if (Test-Path -LiteralPath $ZipPath) {
    Write-Host "Cleaning existing ZIP archive: $ZipPath"
    Remove-Item -LiteralPath $ZipPath -Force
}

if (-not (Test-Path -LiteralPath $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

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

Write-Host "Creating ZIP archive from output directory contents..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($OutputDir, $ZipPath)

if (-not (Test-Path -LiteralPath $ZipPath)) {
    throw "ZIP archive was not created at expected path: $ZipPath"
}

$ZipItem = Get-Item -LiteralPath $ZipPath
$ZipSize = $ZipItem.Length
$HashResult = Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256
$Sha256 = $HashResult.Hash

$FinalOutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$FinalExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$FinalZipPath = (Resolve-Path -LiteralPath $ZipPath).Path

Write-Host "Publish completed successfully." -ForegroundColor Green
Write-Host "Publish directory: $FinalOutputDir" -ForegroundColor Green
Write-Host "Executable path:   $FinalExecutablePath" -ForegroundColor Green
Write-Host "ZIP path:          $FinalZipPath" -ForegroundColor Green
Write-Host "ZIP size (bytes):  $ZipSize" -ForegroundColor Green
Write-Host "ZIP SHA-256:       $Sha256" -ForegroundColor Green
