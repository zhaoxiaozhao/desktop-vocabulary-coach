using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Review;

/// <summary>
/// 简化 SM-2 实现（可整体替换为 FSRS）：
/// - 认识（Good）：1 天 → 3 天 → 上次间隔 × 难度系数；
/// - 模糊（Hard）：短间隔（上次 × 1.2，至少 1 天），难度系数小幅下降；
/// - 不认识（Again）：重复次数归零，10 分钟后重学，难度系数下降并累计遗忘次数；
/// - 难度系数限制在 1.3–3.0，间隔限制在 10 分钟–365 天。
/// </summary>
public sealed class Sm2ReviewScheduler : IReviewScheduler
{
    public const double DefaultEaseFactor = 2.5;
    public const double MinEaseFactor = 1.3;
    public const double MaxEaseFactor = 3.0;
    public const double MaxIntervalDays = 365;
    public const double RelearnIntervalDays = 10.0 / (24 * 60);
    public const double MasteredIntervalDays = 60;

    public ReviewScheduleResult Schedule(ReviewState state, ReviewRating rating, DateTimeOffset reviewedAt)
    {
        var repetition = Math.Max(0, state.Repetition);
        var easeFactor = IsValidEase(state.EaseFactor) ? state.EaseFactor : DefaultEaseFactor;
        var intervalDays = Math.Max(0, state.IntervalDays);
        var lapseCount = Math.Max(0, state.LapseCount);

        switch (rating)
        {
            case ReviewRating.Again:
                repetition = 0;
                lapseCount++;
                intervalDays = RelearnIntervalDays;
                easeFactor = Math.Max(MinEaseFactor, easeFactor - 0.20);
                break;

            case ReviewRating.Hard:
                repetition = Math.Max(1, repetition);
                intervalDays = repetition <= 1 ? 1 : Math.Max(1, intervalDays * 1.2);
                easeFactor = Math.Max(MinEaseFactor, easeFactor - 0.15);
                break;

            case ReviewRating.Good:
                repetition++;
                intervalDays = repetition switch
                {
                    1 => 1,
                    2 => 3,
                    _ => Math.Max(3, intervalDays * easeFactor),
                };
                break;

            case ReviewRating.Easy:
                repetition++;
                easeFactor = Math.Min(MaxEaseFactor, easeFactor + 0.15);
                intervalDays = repetition switch
                {
                    1 => 2,
                    _ => Math.Max(4, intervalDays * easeFactor * 1.3),
                };
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(rating), rating, "未知的复习评分。");
        }

        intervalDays = Math.Clamp(intervalDays, RelearnIntervalDays, MaxIntervalDays);
        easeFactor = Math.Clamp(easeFactor, MinEaseFactor, MaxEaseFactor);

        var status = intervalDays switch
        {
            >= MasteredIntervalDays => WordProgressStatus.Mastered,
            >= 3 => WordProgressStatus.Review,
            _ => WordProgressStatus.Learning,
        };

        return new ReviewScheduleResult(
            rating,
            repetition,
            easeFactor,
            intervalDays,
            lapseCount,
            status,
            reviewedAt.AddDays(intervalDays));
    }

    private static bool IsValidEase(double easeFactor) =>
        easeFactor is >= MinEaseFactor and <= MaxEaseFactor;
}
