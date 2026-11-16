namespace VocabularyCoach.Application.Statistics;

/// <summary>今日学习统计，所有数字直接来自 LearningSessions / ReviewLogs / UserWordProgress。</summary>
public sealed record TodayStatistics(
    DateOnly Date,
    int TotalSessions,
    int Recognized,
    int Vague,
    int Unknown,
    int Dismissed,
    int Snoozed,
    int Timeout,
    int DueReviewCount,
    int WordsLearnedToday)
{
    public int MasteryFeedbackCount => Recognized + Vague + Unknown;
}

/// <summary>今日学习统计查询。</summary>
public interface IStatisticsService
{
    Task<TodayStatistics> GetTodayAsync(CancellationToken cancellationToken);
}
