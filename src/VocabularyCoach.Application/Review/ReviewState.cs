using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Review;

/// <summary>复习算法需要的当前状态。</summary>
public sealed record ReviewState
{
    public int Repetition { get; init; }

    public double EaseFactor { get; init; } = 2.5;

    public double IntervalDays { get; init; }

    public int LapseCount { get; init; }

    public WordProgressStatus Status { get; init; } = WordProgressStatus.New;

    public DateTimeOffset? DueAt { get; init; }

    public static ReviewState New => new();
}
