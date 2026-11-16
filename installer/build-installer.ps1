param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$publishDir = Join-Path $root "artifacts\publish"

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    Write-Output "Publishing self-contained win-x64 build..."
    dotnet publish (Join-Path $root "src\VocabularyCoach.App\VocabularyCoach.App.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:DebugType=none `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

& (Join-Path $root "tools\get-innosetup.ps1")

$iscc = Join-Path $root ".tools\innosetup\ISCC.exe"
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found. Run tools\get-innosetup.ps1 first."
}

Write-Output "Building installer $version..."
& $iscc "/DMyAppVersion=$version" "/DMyPublishDir=$publishDir" (Join-Path $PSScriptRoot "VocabularyCoach.iss")

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Output "Installer written to $(Join-Path $root 'artifacts\installer')"
