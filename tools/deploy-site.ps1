param(
    [string]$ProjectName = "desktop-vocabulary-coach",
    [string]$DownloadUrl = "",
    [switch]$SkipInstallerCheck
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$site = Join-Path $root "site"

if ($DownloadUrl) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "update-site-config.ps1") -DownloadUrl $DownloadUrl
    if ($LASTEXITCODE -ne 0) {
        throw "update-site-config.ps1 执行失败。"
    }
}

$configPath = Join-Path $site "config.js"
$config = Get-Content $configPath -Raw -Encoding UTF8

if ($config -notmatch 'downloadUrl:\s*"https?://') {
    throw "site/config.js 中的 downloadUrl 仍为空。请先把安装包上传到 R2 / GitHub Releases，再用 -DownloadUrl 传入下载地址。"
}

if (-not $SkipInstallerCheck) {
    $props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
    $version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    $installer = Join-Path $root "artifacts\installer\DesktopVocabularyCoach-Setup-$version.exe"

    if (Test-Path $installer) {
        $localHash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        $configHash = [regex]::Match($config, 'sha256:\s*"([^"]+)"').Groups[1].Value

        if ($localHash -ne $configHash) {
            throw "安装包 SHA256 与 site/config.js 不一致。请先运行 tools\update-site-config.ps1 更新配置。"
        }

        Write-Output "本地安装包 SHA256 与站点配置一致：$localHash"
    }
    else {
        Write-Warning "未找到 $installer，跳过本地安装包校验。"
    }
}

Write-Output "正在部署 $site 到 Cloudflare Pages 项目 $ProjectName ..."

Push-Location $root
try {
    npx --yes wrangler pages deploy $site --project-name $ProjectName
    if ($LASTEXITCODE -ne 0) {
        throw "wrangler 部署失败（退出码 $LASTEXITCODE）。"
    }
}
finally {
    Pop-Location
}

Write-Output "部署完成。请在 Cloudflare Dashboard 或本次输出中确认访问地址。"
