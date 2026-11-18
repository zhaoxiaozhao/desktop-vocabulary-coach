# 试用、激活与自动更新

本文档说明 0.9.0 起的授权与更新机制，以及发布新版本的操作流程。

## 1. 30 天全功能试用

- 首次启动开始计时，**30 天全功能**，不需要注册或联网。
- 状态同时写入：
  - `%LocalAppData%\DesktopVocabularyCoach\trial.json`
  - `HKCU\Software\DesktopVocabularyCoach` 的 `TrialFirstStartedAt` / `TrialLastSeenAt`
- 读取时取最早的首启时间；系统时间回拨超过 1 天视为异常，试用直接结束。
- 试用到期后：
  - 自动弹卡、手动弹卡、生词捕获暂停；
  - 主窗口、查词、备份、导出、设置仍然可用；
  - 学习数据不删除、不锁定。
- 主窗口「运行状态」显示剩余天数；托盘菜单和主窗口都有「激活 / 管理」。

## 2. 离线激活码

### 格式

```text
DVC1.<base64url(payload JSON)>.<base64url(ECDSA P-256 signature)>
```

payload 示例：

```json
{"v":1,"id":"XHS-0001","edition":"lifetime","iat":"2026-11-16T08:00:00+00:00","note":"xiaohongshu"}
```

- App 内置公钥，只能验签，不能生成。
- 激活码不绑定机器，换电脑可直接重新粘贴。
- 激活后保存到 `%LocalAppData%\DesktopVocabularyCoach\license.dat`（DPAPI 当前用户加密）。
- 不联网、不上传、不读取硬件信息。

### 私钥

私钥由 `tools/LicenseTool` 生成，默认位置：

```text
%USERPROFILE%\.dvc\signing-key.pem
```

> **必须离线备份。** 私钥丢失后无法再签发新的激活码或更新清单，只能发布新版本更换公钥。
> 私钥不得提交到仓库、网盘或聊天记录。

### 生成与验证

```powershell
# 首次：生成密钥（已生成过会拒绝覆盖）
dotnet run --project tools\LicenseTool -- init

# 生成一个激活码
dotnet run --project tools\LicenseTool -- license --id XHS-0001 --note "xiaohongshu 订单 001"

# 验证激活码
dotnet run --project tools\LicenseTool -- verify-license --license "DVC1...."
```

`--edition` 默认 `lifetime`。`--id` 建议使用「渠道-序号」，方便对账和日后吊销。

### 发货

激活码约 230 字符，建议：

1. 自动回复里放完整激活码（代码块，方便一键复制）；
2. 同时把激活码保存为 `订单号.txt` 放进网盘交付目录；
3. 附上安装包 SHA256 和 SmartScreen 提示说明。

## 3. 自动更新

### 检查策略

- 启动 30 秒后检查一次，之后每 24 小时检查一次（可在 `appsettings.json` 的 `Update` 节调整）。
- 发现新版本：托盘气泡提示，不强制弹窗；用户从托盘「检查更新…」查看。
- 手动检查：托盘菜单「检查更新…」。
- 下载完成后校验 SHA256，再以
  `DesktopVocabularyCoach-Setup-x.y.z.exe /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /UPDATE`
  静默安装，安装包自动重启应用。

### 清单格式

`updates/latest.json`（签名信封）：

```json
{
  "payload": "<base64url(payload JSON)>",
  "signature": "<base64url(ECDSA P-256 signature)>"
}
```

payload 示例：

```json
{
  "v": 1,
  "version": "0.9.1",
  "releasedAt": "2026-12-01",
  "notes": ["修复 ...", "新增 ..."],
  "url": "https://<bucket>.oss-cn-hangzhou.aliyuncs.com/releases/0.9.1/DesktopVocabularyCoach-Setup-0.9.1.exe",
  "fallbackUrl": "https://github.com/zhaoxiaozhao/desktop-vocabulary-coach/releases/download/v0.9.1/DesktopVocabularyCoach-Setup-0.9.1.exe",
  "sha256": "<64 位小写十六进制>",
  "size": 56731677,
  "mandatory": false
}
```

- `url` 必须是 HTTPS；`sha256` 与 `size` 必须与实际安装包一致。
- App 验签失败、URL 非 HTTPS、SHA 格式不正确都会拒绝更新。
- 清单本身通过 HTTPS 获取；签名保证即使镜像被篡改也无法下发恶意安装包。

### 生成清单

```powershell
$installer = "artifacts\installer\DesktopVocabularyCoach-Setup-0.9.1.exe"
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $installer).Length

dotnet run --project tools\LicenseTool -- manifest `
  --version 0.9.1 `
  --url "https://<bucket>.oss-cn-hangzhou.aliyuncs.com/releases/0.9.1/DesktopVocabularyCoach-Setup-0.9.1.exe" `
  --fallback "https://github.com/zhaoxiaozhao/desktop-vocabulary-coach/releases/download/v0.9.1/DesktopVocabularyCoach-Setup-0.9.1.exe" `
  --sha256 $hash `
  --size $size `
  --notes "更新说明 1|更新说明 2" `
  --output updates\latest.json
```

### 发布顺序

1. 修改 `Directory.Build.props` 版本号；
2. `dotnet build` / `dotnet test`；
3. `installer\build-installer.ps1`；
4. 用上面的命令生成 `updates/latest.json`（OSS URL 优先，GitHub 兜底）；
5. 上传安装包与清单到 OSS；
6. 创建 GitHub Release 并上传同一个安装包；
7. 更新 `site/config.js`（版本、大小、SHA256、下载地址）；
8. `tools\deploy-site.ps1` 部署官网；
9. 提交 `updates/latest.json` 与文档改动。

### 应用内更新地址

`src/VocabularyCoach.App/appsettings.json`：

```json
"Update": {
  "CheckDelaySeconds": 30,
  "CheckIntervalHours": 24,
  "ManifestUrls": [
    "https://<bucket>.oss-cn-hangzhou.aliyuncs.com/updates/latest.json",
    "https://github.com/zhaoxiaozhao/desktop-vocabulary-coach/releases/latest/download/latest.json",
    "https://raw.githubusercontent.com/zhaoxiaozhao/desktop-vocabulary-coach/main/updates/latest.json"
  ]
}
```

> 每次发版请把签名的 `latest.json` 作为 Release 资产一起上传；
> `releases/latest/download/latest.json` 会始终指向最新 Release，适合作为 GitHub 兜底。

按顺序尝试，OSS 放第一位（中国大陆更稳定），GitHub 作为兜底。

## 4. 阿里云 OSS 镜像

需要准备：

- Bucket（公共读），例如 `vocabulary-coach-downloads`；
- 地域/Endpoint，例如 `oss-cn-hangzhou.aliyuncs.com`；
- 目录约定：
  ```text
  releases/<version>/DesktopVocabularyCoach-Setup-<version>.exe
  updates/latest.json
  ```
- 专用于上传的 RAM 用户，最小权限：
  ```json
  {
    "Version": "1",
    "Statement": [
      {
        "Effect": "Allow",
        "Action": ["oss:PutObject", "oss:GetObject", "oss:ListObjects"],
        "Resource": [
          "acs:oss:*:*:vocabulary-coach-downloads",
          "acs:oss:*:*:vocabulary-coach-downloads/*"
        ]
      }
    ]
  }
  ```

上传示例（ossutil 已配置）：

```powershell
ossutil cp "artifacts\installer\DesktopVocabularyCoach-Setup-0.9.1.exe" `
  "oss://vocabulary-coach-downloads/releases/0.9.1/DesktopVocabularyCoach-Setup-0.9.1.exe" -f

ossutil cp "updates\latest.json" `
  "oss://vocabulary-coach-downloads/updates/latest.json" `
  -f --meta "Content-Type:application/json"
```

- 使用 OSS 默认域名不需要备案；绑定自有域名到中国大陆 OSS 需要 ICP 备案。
- 安装包与清单可公开读；不要在该 Bucket 放数据库、日志、API Key 或源码。
