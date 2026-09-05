[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$ProjectFile = Join-Path $RepoRoot "src\BootCampPerformanceControl\BootCampPerformanceControl.csproj"
$PublishProfile = Join-Path $RepoRoot "src\BootCampPerformanceControl\Properties\PublishProfiles\win-x64-release.pubxml"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$LicenseFile = Join-Path $RepoRoot "LICENSE"
$ThirdPartyFile = Join-Path $RepoRoot "THIRD_PARTY.md"

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
$ZipSha256Path = "$ZipPath.sha256"

if (-not (Test-Path -LiteralPath $PublishProfile)) {
    throw "Publish profile not found: $PublishProfile"
}

foreach ($requiredFile in @($LicenseFile, $ThirdPartyFile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release file not found: $requiredFile"
    }
}

Write-Host "=== BootCamp Performance Control $Version Release Publish (win-x64) ===" -ForegroundColor Cyan
Write-Host "Repository root: $RepoRoot"
Write-Host "Project file:    $ProjectFile"
Write-Host "Publish profile: $PublishProfile"
Write-Host "Version:         $Version"
Write-Host "Output path:     $OutputDir"
Write-Host "ZIP path:        $ZipPath"
Write-Host "ZIP hash path:   $ZipSha256Path"

# Clean only the dedicated versioned publish directory and ZIP if they already exist
if (Test-Path -LiteralPath $OutputDir) {
    Write-Host "Cleaning output directory: $OutputDir"
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

if (Test-Path -LiteralPath $ZipPath) {
    Write-Host "Cleaning existing ZIP archive: $ZipPath"
    Remove-Item -LiteralPath $ZipPath -Force
}

if (Test-Path -LiteralPath $ZipSha256Path) {
    Write-Host "Cleaning existing ZIP hash: $ZipSha256Path"
    Remove-Item -LiteralPath $ZipSha256Path -Force
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

Copy-Item -LiteralPath $LicenseFile -Destination (Join-Path $OutputDir "LICENSE") -Force
Copy-Item -LiteralPath $ThirdPartyFile -Destination (Join-Path $OutputDir "THIRD_PARTY.md") -Force

$ProhibitedExtensions = @(".sys", ".inf", ".cat")
$ProhibitedNames = @("macsfancontrol_setup.exe", "MacsFanControl.exe", "applesmc.sys")
$ProhibitedFiles = @(
    Get-ChildItem -LiteralPath $OutputDir -Recurse -File | Where-Object {
        $file = $_
        $extensionBlocked = @($ProhibitedExtensions | Where-Object {
            [string]::Equals($_, $file.Extension, [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
        $nameBlocked = @($ProhibitedNames | Where-Object {
            [string]::Equals($_, $file.Name, [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0
        $extensionBlocked -or $nameBlocked
    }
)

if ($ProhibitedFiles.Count -gt 0) {
    $blockedPaths = $ProhibitedFiles.FullName -join [Environment]::NewLine
    throw "Release publish directory contains prohibited driver or Macs Fan Control content:$([Environment]::NewLine)$blockedPaths"
}

Write-Host "Release-content safety scan passed. No prohibited driver or Macs Fan Control files were found."

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
"$Sha256  $($ZipItem.Name)" | Set-Content -LiteralPath $ZipSha256Path -Encoding ascii

if (-not (Test-Path -LiteralPath $ZipSha256Path -PathType Leaf)) {
    throw "ZIP SHA-256 file was not created at expected path: $ZipSha256Path"
}

$FinalOutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$FinalExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$FinalZipPath = (Resolve-Path -LiteralPath $ZipPath).Path
$FinalZipSha256Path = (Resolve-Path -LiteralPath $ZipSha256Path).Path

Write-Host "Publish completed successfully." -ForegroundColor Green
Write-Host "Publish directory: $FinalOutputDir" -ForegroundColor Green
Write-Host "Executable path:   $FinalExecutablePath" -ForegroundColor Green
Write-Host "ZIP path:          $FinalZipPath" -ForegroundColor Green
Write-Host "ZIP size (bytes):  $ZipSize" -ForegroundColor Green
Write-Host "ZIP SHA-256:       $Sha256" -ForegroundColor Green
Write-Host "ZIP hash file:     $FinalZipSha256Path" -ForegroundColor Green
