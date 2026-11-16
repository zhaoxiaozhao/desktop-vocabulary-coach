using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>UserWordProgress 表：每个单词一行用户进度，WordId 唯一。</summary>
public sealed class UserWordProgressRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public WordRecord? Word { get; set; }

    public WordProgressStatus Status { get; set; } = WordProgressStatus.New;

    /// <summary>连续成功次数（SM-2 的 repetition）。</summary>
    public int Repetition { get; set; }

    public double EaseFactor { get; set; } = 2.5;

    public double IntervalDays { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? LastReviewedAt { get; set; }

    public int LapseCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
