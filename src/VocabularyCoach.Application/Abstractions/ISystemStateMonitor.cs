using VocabularyCoach.Application.Scheduling;

namespace VocabularyCoach.Application.Abstractions;

/// <summary>
/// 系统状态检测（空闲、锁屏、全屏）。由 Infrastructure 用 Windows API 实现，
/// 避免 P/Invoke 散落在业务代码里。
/// </summary>
public interface ISystemStateMonitor
{
    Task<SystemState> GetStateAsync(CancellationToken cancellationToken);
}
