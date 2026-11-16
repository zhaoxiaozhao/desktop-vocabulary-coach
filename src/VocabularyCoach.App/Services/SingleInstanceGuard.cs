namespace VocabularyCoach.App.Services;

/// <summary>
/// 单实例保护：常驻托盘应用被重复启动时会造成多套托盘图标与重复弹窗。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\DesktopVocabularyCoach.SingleInstance";

    private readonly Mutex _mutex;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    /// <summary>当前进程是否为主实例。</summary>
    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
