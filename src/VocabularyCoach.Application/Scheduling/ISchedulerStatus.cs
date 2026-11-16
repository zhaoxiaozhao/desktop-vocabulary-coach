namespace VocabularyCoach.Application.Scheduling;

/// <summary>供 UI 展示调度器状态（只读）。</summary>
public interface ISchedulerStatus
{
    /// <summary>下一次调度检查时间（本地时间）；暂停或未启动时为 null。</summary>
    DateTimeOffset? NextCheckAt { get; }

    /// <summary>一句话状态描述。</summary>
    string DescribeStatus();
}
