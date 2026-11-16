# 静态官网（Cloudflare Pages）

纯静态站点：无构建步骤、无第三方脚本、无外链字体。直接部署 `site/` 目录即可。

## 先配置下载地址

编辑 `site/config.js`：

```js
window.VOCAB_CONFIG = {
  version: "0.8.2",
  size: "54.1 MB",
  sha256: "…",
  downloadUrl: "https://download.example.com/DesktopVocabularyCoach-Setup-0.8.2.exe",
  updatedAt: "2026-11-16",
};
```

也可以使用脚本自动读取 `Directory.Build.props` 的版本、安装包 SHA256 与大小：

```powershell
powershell -ExecutionPolicy Bypass -File tools\update-site-config.ps1 `
  -DownloadUrl "https://download.example.com/DesktopVocabularyCoach-Setup-0.8.2.exe"
```

## 为什么安装包不能放在 Pages

Cloudflare Pages 限制**单文件 25 MiB**（Free/Pro/Business 相同），当前安装包约 56 MB。
Cloudflare 官方建议把大文件放到 **R2 公共桶**，页面再从 R2 下载。

可选方案：

1. **Cloudflare R2（推荐）**
   - 创建 Bucket，例如 `vocabulary-coach-downloads`。
   - 上传 `artifacts/installer/DesktopVocabularyCoach-Setup-0.8.2.exe`。
   - 在 Bucket → Settings → Public access 开启 `r2.dev` 公共地址，或绑定自定义域名
     （例如 `download.example.com`）。
   - 把公开 URL 填入 `config.js` 的 `downloadUrl`。
   - R2 免费额度包含存储与公网出口流量（以 Cloudflare 当前定价为准）。
2. **GitHub Releases**
   - 在仓库创建 Release，上传安装包，复制资产下载链接填入 `config.js`。
   - 优点：简单；缺点：中国大陆访问 GitHub Releases 可能不稳定。
3. **国内对象存储 + CDN**
   - 如果主要面向中国大陆用户，阿里云 OSS / 腾讯云 COS + CDN 通常更稳定，
     但绑定自定义域名需要 ICP 备案。

## 部署到 Cloudflare Pages

### 方式 A：控制台直接上传

1. Cloudflare Dashboard → Workers & Pages → Create → Pages → Upload assets。
2. 上传 `site/` 目录（**不要把安装包放进 site**）。
3. 得到 `https://<project>.pages.dev` 地址。

### 方式 B：Wrangler（推荐用封装脚本）

一键更新配置 + 校验 SHA256 + 部署：

```powershell
powershell -ExecutionPolicy Bypass -File tools\deploy-site.ps1 `
  -DownloadUrl "https://download.example.com/DesktopVocabularyCoach-Setup-0.8.2.exe"
```

脚本会在 `downloadUrl` 为空、或本地安装包 SHA256 与 `config.js` 不一致时直接中止，
避免发布一个下载按钮点不开或校验值对不上的页面。

也可以直接调用 wrangler：

```powershell
npx wrangler pages deploy site --project-name desktop-vocabulary-coach
```

首次会要求登录 Cloudflare。之后每次更新站点重新执行即可。

### 自定义域名

Pages 项目 → Custom domains → 添加域名（DNS 需托管在 Cloudflare）。
中国大陆访问 `pages.dev` 可能不稳定，建议绑定自有域名并测试；如面向大陆用户且要求稳定，
考虑国内对象存储 + CDN（需备案）。

## 本地预览

```powershell
cd site
python -m http.server 8123
# 打开 http://localhost:8123
```

或使用 `npx serve site`。

## 文件说明

| 文件 | 说明 |
| --- | --- |
| `index.html` | 落地页（功能、截图、安装、隐私、FAQ） |
| `styles.css` | 全部样式，响应式，深色主题 |
| `app.js` | 填版本/大小/SHA256、配置下载链接、复制校验值 |
| `config.js` | **部署前唯一需要修改的文件** |
| `assets/` | 应用截图 |
| `_headers` | Cloudflare Pages 安全响应头 |
| `robots.txt` | 搜索引擎规则 |

## 安全建议

- 发布前用 `Get-FileHash <installer> -Algorithm SHA256` 核对页面上的校验值。
- 安装包未代码签名，SmartScreen 会提示；页面上已注明「更多信息 → 仍要运行」。
- 不要把 API Key、数据库、日志或源码打包进站点。
- 页面无统计脚本；如需统计可自行加 Cloudflare Web Analytics（无 Cookie）。
