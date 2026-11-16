using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Repositories;
using VocabularyCoach.Infrastructure.Tests.Support;

namespace VocabularyCoach.Infrastructure.Tests.Learning;

public class EfLearningStoreTests
{
    [Fact]
    public async Task RecognizedFeedback_PersistsProgressReviewLogAndSession()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database, "apple");
        var service = CreateService(database, wordId, WordCardResult.Recognized);

        var before = DateTimeOffset.UtcNow;
        var result = await service.ShowNextCardAsync(LearningSource.Manual);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(WordCardResult.Recognized, result);

        await using var db = await database.Factory.CreateDbContextAsync();

        var progress = await db.UserWordProgress.SingleAsync(p => p.WordId == wordId);
        Assert.Equal(WordProgressStatus.Learning, progress.Status);
        Assert.Equal(1, progress.Repetition);
        Assert.Equal(1, progress.IntervalDays, 3);
        Assert.Equal(2.5, progress.EaseFactor, 3);
        Assert.Equal(0, progress.LapseCount);
        Assert.InRange(progress.DueAt!.Value, before.AddDays(1), after.AddDays(1));

        var reviewLog = await db.ReviewLogs.SingleAsync();
        Assert.Equal(ReviewRating.Good, reviewLog.Rating);
        Assert.Equal(WordFeedback.Recognized, reviewLog.UserFeedback);
        Assert.Equal(LearningSource.Manual, reviewLog.Source);
        Assert.Equal(progress.DueAt, reviewLog.NextDueAt);
        Assert.Null(reviewLog.PreviousDueAt);

        var session = await db.LearningSessions.SingleAsync();
        Assert.Equal(WordCardResult.Recognized, session.Action);
        Assert.Equal(LearningSource.Manual, session.Source);
        Assert.NotNull(session.RespondedAt);
        Assert.True(session.DurationMs >= 0);
        Assert.True(session.RespondedAt >= session.ShownAt);
    }

    [Fact]
    public async Task DismissedFeedback_RecordsSessionOnly()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database, "apple");
        var service = CreateService(database, wordId, WordCardResult.Dismissed);

        await service.ShowNextCardAsync(LearningSource.Popup);

        await using var db = await database.Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.UserWordProgress.CountAsync());
        Assert.Equal(0, await db.ReviewLogs.CountAsync());

        var session = await db.LearningSessions.SingleAsync();
        Assert.Equal(WordCardResult.Dismissed, session.Action);
        Assert.Equal(LearningSource.Popup, session.Source);
    }

    [Fact]
    public async Task UnknownFeedback_ResetsProgress_AndCountsLapse()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database, "resilient");

        var recognized = CreateService(database, wordId, WordCardResult.Recognized);
        await recognized.ShowNextCardAsync(LearningSource.Manual);

        var unknown = CreateService(database, wordId, WordCardResult.Unknown);
        var before = DateTimeOffset.UtcNow;
        await unknown.ShowNextCardAsync(LearningSource.Manual);

        await using var db = await database.Factory.CreateDbContextAsync();
        var progress = await db.UserWordProgress.SingleAsync(p => p.WordId == wordId);
        Assert.Equal(WordProgressStatus.Learning, progress.Status);
        Assert.Equal(0, progress.Repetition);
        Assert.Equal(1, progress.LapseCount);
        Assert.Equal(10.0 / (24 * 60), progress.IntervalDays, 5);
        Assert.InRange(progress.DueAt!.Value, before.AddMinutes(9), before.AddMinutes(11));

        var logs = await db.ReviewLogs.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal(ReviewRating.Good, logs[0].Rating);
        Assert.Equal(ReviewRating.Again, logs[1].Rating);

        Assert.Equal(2, await db.LearningSessions.CountAsync());
    }

    [Fact]
    public async Task SecondRecognizedFeedback_ExtendsIntervalToThreeDays()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database, "momentum");

        await CreateService(database, wordId, WordCardResult.Recognized).ShowNextCardAsync();
        await CreateService(database, wordId, WordCardResult.Recognized).ShowNextCardAsync();

        await using var db = await database.Factory.CreateDbContextAsync();
        var progress = await db.UserWordProgress.SingleAsync(p => p.WordId == wordId);
        Assert.Equal(2, progress.Repetition);
        Assert.Equal(3, progress.IntervalDays, 3);
        Assert.Equal(WordProgressStatus.Review, progress.Status);
    }

    [Fact]
    public async Task GetDueReviews_ReturnsOnlyDueWords_OrderedByOldestDueFirst()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var recent = await AddWordAsync(database, "recent");
        var overdue = await AddWordAsync(database, "overdue");
        var future = await AddWordAsync(database, "future");

        var now = DateTimeOffset.UtcNow;
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            db.UserWordProgress.AddRange(
                new UserWordProgressRecord
                {
                    WordId = recent,
                    DueAt = now.AddHours(-1),
                    LastReviewedAt = now.AddDays(-2),
                    UpdatedAt = now,
                },
                new UserWordProgressRecord
                {
                    WordId = overdue,
                    DueAt = now.AddHours(-3),
                    LastReviewedAt = now.AddDays(-3),
                    UpdatedAt = now,
                },
                new UserWordProgressRecord
                {
                    WordId = future,
                    DueAt = now.AddHours(5),
                    LastReviewedAt = now,
                    UpdatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        var store = new EfLearningStore(database.Factory);
        var due = await store.GetDueReviewsAsync(now, 10, CancellationToken.None);

        Assert.Equal(2, due.Count);
        Assert.Equal("overdue", due[0].Card.Word);
        Assert.Equal("recent", due[1].Card.Word);
        Assert.True(due[0].Card.Id > 0);
        Assert.NotEmpty(due[0].Card.Definitions);
        Assert.Equal(2, await store.CountDueReviewsAsync(now, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentDifficulty_ReturnsProfileOrDefault()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var store = new EfLearningStore(database.Factory);

        Assert.Equal(2, await store.GetCurrentDifficultyAsync(CancellationToken.None));

        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            db.UserLearningProfiles.Add(new UserLearningProfileRecord
            {
                CurrentDifficulty = 4,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(4, await store.GetCurrentDifficultyAsync(CancellationToken.None));
    }

    private sealed class NoopAdaptiveDifficultyService : VocabularyCoach.Application.Adaptive.IAdaptiveDifficultyService
    {
        public Task<int> ApplyAsync(CancellationToken cancellationToken) => Task.FromResult(2);

        public Task ResetPreferenceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static WordCardService CreateService(
        SqliteTestDatabase database,
        int wordId,
        WordCardResult result)
    {
        var store = new EfLearningStore(database.Factory);
        return new WordCardService(
            new StaticSource(CardWithId(wordId)),
            new StaticPresenter(result),
            store,
            new Sm2ReviewScheduler(),
            new NoopAdaptiveDifficultyService(),
            new NoopAiEnrichmentService(),
            TimeProvider.System,
            NullLogger<WordCardService>.Instance);
    }

    private static WordCard CardWithId(int id) => new()
    {
        Id = id,
        Word = "apple",
        Phonetic = "ˈæpl",
        Definitions = [new WordDefinition { PartOfSpeech = "n.", DefinitionZh = "苹果" }],
    };

    private static async Task<int> AddWordAsync(SqliteTestDatabase database, string word)
    {
        await using var db = await database.Factory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;

        var record = new WordRecord
        {
            Word = word,
            NormalizedWord = word.ToLowerInvariant(),
            Difficulty = 1,
            FrequencyRank = 100,
            Source = "test",
            CreatedAt = now,
            UpdatedAt = now,
            Definitions =
            [
                new WordDefinitionRecord
                {
                    PartOfSpeech = "n.",
                    DefinitionZh = "测试释义",
                    SortOrder = 0,
                },
            ],
        };

        db.Words.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }

    private sealed class NoopAiEnrichmentService : VocabularyCoach.Application.Ai.IAiEnrichmentService
    {
        public Task<VocabularyCoach.Core.Entities.WordEnrichment?> GetCachedAsync(
            int wordId,
            CancellationToken cancellationToken) =>
            Task.FromResult<VocabularyCoach.Core.Entities.WordEnrichment?>(null);

        public void QueueEnrichment(VocabularyCoach.Core.Entities.WordCard card)
        {
        }

        public Task<VocabularyCoach.Core.Entities.WordEnrichment?> EnrichNowAsync(
            VocabularyCoach.Core.Entities.WordCard card,
            CancellationToken cancellationToken) =>
            Task.FromResult<VocabularyCoach.Core.Entities.WordEnrichment?>(null);

        public Task<VocabularyCoach.Application.Ai.AiTestResult> TestAsync(
            string? apiKeyOverride,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VocabularyCoach.Application.Ai.AiTestResult(false, "not configured"));
    }

    private sealed class StaticSource : IWordCardSource
    {
        private readonly WordCard _card;

        public StaticSource(WordCard card) => _card = card;

        public Task<WordCard?> GetNextCardAsync(CancellationToken cancellationToken) =>
            Task.FromResult<WordCard?>(_card);
    }

    private sealed class StaticPresenter : IWordPopupPresenter
    {
        private readonly WordCardResult _result;

        public StaticPresenter(WordCardResult result) => _result = result;

        public Task<WordCardResult> ShowAsync(WordCard card, CancellationToken cancellationToken) =>
            Task.FromResult(_result);

        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
