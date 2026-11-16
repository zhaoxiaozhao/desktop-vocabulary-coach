param(
    [string]$Version = "6.7.3"
)

# 从 NuGet 下载 Inno Setup 编译器（Tools.InnoSetup 包），无需 GitHub 网络。
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root ".tools\innosetup"

if (Test-Path (Join-Path $target "ISCC.exe")) {
    Write-Output "Inno Setup compiler already present: $target"
    exit 0
}

$extract = Join-Path $env:TEMP "innosetup-$Version-extract"
$nupkg = Join-Path $env:TEMP "tools.innosetup.$Version.nupkg"
$zip = Join-Path $env:TEMP "tools.innosetup.$Version.zip"

if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
New-Item -ItemType Directory -Path $extract | Out-Null

$url = "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$Version/tools.innosetup.$Version.nupkg"
Write-Output "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing

Copy-Item $nupkg $zip -Force
Expand-Archive -Path $zip -DestinationPath $extract -Force

$compilerDir = Join-Path $extract "tools"
if (-not (Test-Path (Join-Path $compilerDir "ISCC.exe"))) {
    throw "ISCC.exe not found in downloaded package."
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item (Join-Path $compilerDir "*") $target -Recurse -Force

Write-Output "Inno Setup compiler ready: $target"
