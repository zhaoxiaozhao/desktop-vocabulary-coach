using Microsoft.Win32;
using VocabularyCoach.Application.Abstractions;

namespace VocabularyCoach.Infrastructure.SystemIntegration;

/// <summary>
/// 通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 管理开机启动。
/// 使用当前用户注册表，无需管理员权限；卸载时安装包会清理该值。
/// </summary>
public sealed class WindowsStartupManager : ISystemStartupManager
{
    public const string DefaultValueName = "DesktopVocabularyCoach";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;
    private readonly string? _executablePath;

    public WindowsStartupManager(
        string valueName = DefaultValueName,
        string? executablePath = null)
    {
        _valueName = valueName;
        _executablePath = executablePath ?? Environment.ProcessPath;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(_valueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户启动项注册表键。");

        if (enabled)
        {
            var executablePath = _executablePath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("无法确定当前程序路径，不能设置开机启动。");
            }

            key.SetValue(
                _valueName,
                $"\"{executablePath}\" --startup",
                RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }

    public void Reconcile(bool enabled)
    {
        // 已启用时总是重写路径，避免程序升级/移动后指向旧位置。
        SetEnabled(enabled);
    }
}
