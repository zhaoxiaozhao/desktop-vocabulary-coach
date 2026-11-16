# 安装包构建

使用 [Inno Setup](https://jrsoftware.org/isinfo.php) 生成 Windows 安装包（`installer/VocabularyCoach.iss`）。

## 一键构建

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

脚本会：

1. `dotnet publish` 生成自包含 win-x64 产物到 `artifacts/publish`（目标机器无需预装 .NET）。
2. 从 NuGet 下载 Inno Setup 编译器（`Tools.InnoSetup`）到 `.tools/innosetup`，无需手动安装。
3. 读取 `Directory.Build.props` 的版本号，编译安装包到
   `artifacts/installer/DesktopVocabularyCoach-Setup-<version>.exe`。

只重建安装包（跳过 publish）：

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -SkipPublish
```

## 安装/升级/卸载行为

- 默认安装到 `%LocalAppData%\Programs\Desktop Vocabulary Coach`（按用户安装，无需管理员权限）。
- 已安装时再次运行安装包会原地升级，保留用户数据；升级会自动关闭正在运行的旧版本（不会丢失已提交的学习反馈）。
- 卸载只删除程序文件与快捷方式，**不会删除** `%LocalAppData%\DesktopVocabularyCoach`
  （数据库、日志、API Key 加密文件）。静默卸载时不弹提示。
- 安装包通过 `CloseApplications` 自动关闭正在运行的旧版本，静默升级也能顺利完成。

## 中文语言文件

`installer/languages/ChineseSimplified.isl` 来自 Inno Setup 官方仓库
（`jrsoftware/issrc` 的 `Files/Languages/ChineseSimplified.isl`），遵循 Inno Setup 许可证。

## 已验证

- 0.6.0 安装包成功编译（约 56 MB，自包含）。
- 静默安装 → 启动 → 静默卸载：安装目录删除，用户数据库保留。
