namespace VocabularyCoach.Application.Abstractions;

/// <summary>
/// 开机启动管理（Windows 当前用户 Run 键）。默认关闭，由用户设置控制。
/// </summary>
public interface ISystemStartupManager
{
    bool IsEnabled();

    /// <summary>写入/删除启动项；路径使用当前可执行文件并带 --startup 参数。</summary>
    void SetEnabled(bool enabled);

    /// <summary>按设置同步启动项（应用升级/移动路径后修正命令行）。</summary>
    void Reconcile(bool enabled);
}
