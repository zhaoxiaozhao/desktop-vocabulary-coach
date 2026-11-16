namespace VocabularyCoach.Application.Scheduling;

/// <summary>
/// 一次系统状态快照。任何一项不满足都不允许自动弹出；检测失败时同样保守处理。
/// </summary>
public sealed record SystemState
{
    /// <summary>用户是否已空闲超过阈值。</summary>
    public bool IsIdle { get; init; }

    /// <summary>锁屏、会话断开或安全桌面（UAC）是否处于活动状态。</summary>
    public bool IsSessionLocked { get; init; }

    /// <summary>前台是否有全屏应用（游戏、演示、视频）。</summary>
    public bool IsFullscreenAppActive { get; init; }

    /// <summary>检测过程是否失败（失败时不弹）。</summary>
    public bool DetectionFailed { get; init; }

    /// <summary>实际空闲时长，便于日志和排查。</summary>
    public TimeSpan IdleTime { get; init; }

    public bool IsEligibleForPopup =>
        !IsIdle && !IsSessionLocked && !IsFullscreenAppActive && !DetectionFailed;

    public static SystemState Ok() => new();

    public string Describe()
    {
        if (DetectionFailed)
        {
            return "系统状态检测失败";
        }

        if (IsSessionLocked)
        {
            return "锁屏或会话不可用";
        }

        if (IsFullscreenAppActive)
        {
            return "前台为全屏应用";
        }

        if (IsIdle)
        {
            return $"用户空闲 {IdleTime.TotalMinutes:0.#} 分钟";
        }

        return "状态正常";
    }
}
