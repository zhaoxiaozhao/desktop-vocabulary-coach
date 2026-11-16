using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Application.Settings;

/// <summary>键值型设置存储（SQLite AppSettings 表）。</summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}

/// <summary>
/// 统一设置服务：内存快照 + 数据库持久化。
/// 消费者每次读取最新派生配置，保存后无需重启即可生效。
/// </summary>
public interface ISettingsService
{
    /// <summary>当前用户设置快照（只读使用，修改请先 Clone）。</summary>
    UserSettings Current { get; }

    SchedulerOptions Scheduler { get; }

    RecommendationOptions Recommendation { get; }

    CardDisplayOptions Card { get; }

    QuietHours QuietHours { get; }

    /// <summary>是否已完成首次启动向导。</summary>
    bool IsInitialized { get; }

    /// <summary>设置变更事件（在保存成功后触发）。</summary>
    event EventHandler? Changed;

    /// <summary>从数据库加载设置；失败时保留 appsettings 默认值。</summary>
    Task LoadAsync(CancellationToken cancellationToken);

    /// <summary>校验并保存设置，同时更新派生配置并触发 <see cref="Changed"/>。</summary>
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken);

    /// <summary>标记首次启动向导已完成。</summary>
    Task MarkInitializedAsync(CancellationToken cancellationToken);
}
