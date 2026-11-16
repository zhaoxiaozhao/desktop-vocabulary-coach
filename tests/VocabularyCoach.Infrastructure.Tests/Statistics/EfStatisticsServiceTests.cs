using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Statistics;
using VocabularyCoach.Infrastructure.Tests.Support;

namespace VocabularyCoach.Infrastructure.Tests.Statistics;

public class EfStatisticsServiceTests
{
    [Fact]
    public async Task GetTodayAsync_MatchesLearningSessions()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var wordId = await AddWordAsync(database);
        var now = DateTimeOffset.UtcNow;

        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            db.LearningSessions.AddRange(
                Session(wordId, now.AddMinutes(-30), WordCardResult.Recognized, LearningSource.Popup),
                Session(wordId, now.AddMinutes(-20), WordCardResult.Recognized, LearningSource.Popup),
                Session(wordId, now.AddMinutes(-15), WordCardResult.Vague, LearningSource.Popup),
                Session(wordId, now.AddMinutes(-10), WordCardResult.Unknown, LearningSource.Manual),
                Session(wordId, now.AddMinutes(-8), WordCardResult.Dismissed, LearningSource.Manual),
                Session(wordId, now.AddMinutes(-5), WordCardResult.Timeout, LearningSource.Popup),
                Session(wordId, now.AddMinutes(-2), WordCardResult.Snoozed, LearningSource.Manual),
                // 昨天的数据不应计入
                Session(wordId, now.AddDays(-1), WordCardResult.Recognized, LearningSource.Popup));

            db.UserWordProgress.Add(new UserWordProgressRecord
            {
                WordId = wordId,
                DueAt = now.AddMinutes(-1),
                LastReviewedAt = now.AddMinutes(-20),
                UpdatedAt = now,
            });

            await db.SaveChangesAsync();
        }

        var service = new EfStatisticsService(database.Factory, TimeProvider.System);
        var stats = await service.GetTodayAsync(CancellationToken.None);

        Assert.Equal(7, stats.TotalSessions);
        Assert.Equal(2, stats.Recognized);
        Assert.Equal(1, stats.Vague);
        Assert.Equal(1, stats.Unknown);
        Assert.Equal(1, stats.Dismissed);
        Assert.Equal(1, stats.Timeout);
        Assert.Equal(1, stats.Snoozed);
        Assert.Equal(1, stats.DueReviewCount);
        Assert.Equal(1, stats.WordsLearnedToday);
        Assert.Equal(4, stats.MasteryFeedbackCount);
    }

    private static async Task<int> AddWordAsync(SqliteTestDatabase database)
    {
        await using var db = await database.Factory.CreateDbContextAsync();
        var word = new WordRecord
        {
            Word = "apple",
            NormalizedWord = "apple",
            Difficulty = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Words.Add(word);
        await db.SaveChangesAsync();
        return word.Id;
    }

    private static LearningSessionRecord Session(
        int wordId,
        DateTimeOffset shownAt,
        WordCardResult action,
        LearningSource source) => new()
    {
        WordId = wordId,
        ShownAt = shownAt,
        RespondedAt = shownAt.AddSeconds(3),
        Action = action,
        DurationMs = 3000,
        Source = source,
    };
}
