using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Review;

/// <summary>一次复习调度的结果，用于更新用户进度。</summary>
public sealed record ReviewScheduleResult(
    ReviewRating Rating,
    int Repetition,
    double EaseFactor,
    double IntervalDays,
    int LapseCount,
    WordProgressStatus Status,
    DateTimeOffset DueAt);

/// <summary>
/// 复习间隔算法抽象。第一版实现简化 SM-2，后续可替换为 FSRS，
/// 复习逻辑不得写进 ViewModel 或 Repository。
/// </summary>
public interface IReviewScheduler
{
    ReviewScheduleResult Schedule(ReviewState state, ReviewRating rating, DateTimeOffset reviewedAt);
}
