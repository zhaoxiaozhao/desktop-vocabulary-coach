namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>UserLearningProfile 表：单行，保存难度估计与最近反馈统计。</summary>
public sealed class UserLearningProfileRecord
{
    public int Id { get; set; }

    /// <summary>当前新词难度 1-4，Sprint 5 自适应会调整该值。</summary>
    public int CurrentDifficulty { get; set; } = 2;

    /// <summary>水平估计（0-1），由反馈统计推算，可为空。</summary>
    public double? LevelEstimate { get; set; }

    public int RecentSampleSize { get; set; }

    public int RecentRecognized { get; set; }

    public int RecentVague { get; set; }

    public int RecentUnknown { get; set; }

    /// <summary>自适应调整后的新词比例偏好；null 表示使用设置中的基础比例。</summary>
    public double? PreferredNewWordRatio { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
