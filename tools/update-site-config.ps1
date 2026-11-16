param(
    [string]$DownloadUrl = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$installer = Join-Path $root "artifacts\installer\DesktopVocabularyCoach-Setup-$version.exe"

if (-not (Test-Path $installer)) {
    throw "Installer not found: $installer. Build it first with installer\build-installer.ps1"
}

$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = [math]::Round((Get-Item $installer).Length / 1MB, 1)
$configPath = Join-Path $root "site\config.js"

if (-not $DownloadUrl -and (Test-Path $configPath)) {
    $current = Get-Content $configPath -Raw
    $match = [regex]::Match($current, 'downloadUrl:\s*"([^"]*)"')
    if ($match.Success) {
        $DownloadUrl = $match.Groups[1].Value
    }
}

$content = @"
// 站点配置：部署前修改 downloadUrl。
// 安装包不能放在 Cloudflare Pages（单文件上限 25 MiB），请使用 R2 公共桶或 GitHub Releases。
window.VOCAB_CONFIG = {
  version: "$version",
  size: "$sizeMb MB",
  sha256: "$hash",
  // 例如：https://download.example.com/DesktopVocabularyCoach-Setup-$version.exe
  downloadUrl: "$DownloadUrl",
  updatedAt: "$(Get-Date -Format yyyy-MM-dd)",
};
"@

[IO.File]::WriteAllText($configPath, $content, (New-Object System.Text.UTF8Encoding($false)))

Write-Output "Updated $configPath"
Write-Output "version=$version size=$sizeMb MB"
Write-Output "sha256=$hash"
if (-not $DownloadUrl) {
    Write-Warning "downloadUrl is empty. Set it with -DownloadUrl after uploading the installer to R2/GitHub."
}
