using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Statistics;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure.Persistence;

namespace VocabularyCoach.Infrastructure.Statistics;

/// <summary>今日统计查询（本地日界线换算为 UTC）。</summary>
public sealed class EfStatisticsService : IStatisticsService
{
    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;

    public EfStatisticsService(
        IDbContextFactory<VocabularyCoachDbContext> dbContextFactory,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
    }

    public async Task<TodayStatistics> GetTodayAsync(CancellationToken cancellationToken)
    {
        var localNow = _timeProvider.GetLocalNow();
        var date = DateOnly.FromDateTime(localNow.DateTime);
        var dayStartLocal = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);
        var dayStartUtc = dayStartLocal.ToUniversalTime();
        var dayEndUtc = dayStartLocal.AddDays(1).ToUniversalTime();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var grouped = await db.LearningSessions
            .AsNoTracking()
            .Where(x => x.ShownAt >= dayStartUtc && x.ShownAt < dayEndUtc)
            .GroupBy(x => x.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int Count(WordCardResult action) =>
            grouped.FirstOrDefault(x => x.Action == action)?.Count ?? 0;

        var nowUtc = _timeProvider.GetUtcNow();
        var dueCount = await db.UserWordProgress
            .AsNoTracking()
            .CountAsync(x => x.DueAt != null && x.DueAt <= nowUtc, cancellationToken)
            .ConfigureAwait(false);

        var learnedToday = await db.UserWordProgress
            .AsNoTracking()
            .CountAsync(
                x => x.LastReviewedAt != null &&
                     x.LastReviewedAt >= dayStartUtc &&
                     x.LastReviewedAt < dayEndUtc,
                cancellationToken)
            .ConfigureAwait(false);

        return new TodayStatistics(
            date,
            grouped.Sum(x => x.Count),
            Count(WordCardResult.Recognized),
            Count(WordCardResult.Vague),
            Count(WordCardResult.Unknown),
            Count(WordCardResult.Dismissed),
            Count(WordCardResult.Snoozed),
            Count(WordCardResult.Timeout),
            dueCount,
            learnedToday);
    }
}
