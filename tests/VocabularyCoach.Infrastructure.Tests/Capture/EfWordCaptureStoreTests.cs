using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Repositories;
using VocabularyCoach.Infrastructure.Tests.Support;

namespace VocabularyCoach.Infrastructure.Tests.Capture;

public class EfWordCaptureStoreTests
{
    [Fact]
    public async Task CreateWordWithProgress_CreatesWordDefinitionAndDueProgress()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var store = new EfWordCaptureStore(database.Factory);

        var wordId = await store.CreateWordWithProgressAsync("take off", "take off", "起飞；脱下", CancellationToken.None);

        await using var db = await database.Factory.CreateDbContextAsync();
        var word = await db.Words.Include(x => x.Definitions).SingleAsync(x => x.Id == wordId);
        Assert.Equal("take off", word.Word);
        Assert.Equal("captured", word.Source, ignoreCase: true);
        Assert.Equal("起飞；脱下", word.Definitions.Single().DefinitionZh);

        var progress = await db.UserWordProgress.SingleAsync(x => x.WordId == wordId);
        Assert.NotNull(progress.DueAt);
        Assert.True(progress.DueAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RecordCapture_UpsertsAndIncrementsCount()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var store = new EfWordCaptureStore(database.Factory);
        var wordId = await store.CreateWordWithProgressAsync("take off", "take off", "起飞", CancellationToken.None);

        await store.RecordCaptureAsync("Take off", "take off", wordId, CancellationToken.None);
        await store.RecordCaptureAsync("take off", "take off", wordId, CancellationToken.None);

        await using var db = await database.Factory.CreateDbContextAsync();
        var capture = await db.WordCaptures.SingleAsync();
        Assert.Equal("take off", capture.NormalizedWord);
        Assert.Equal(2, capture.CaptureCount);
        Assert.Equal("take off", capture.RawText);
    }

    [Fact]
    public async Task GetCaptured_ReturnsDefinitionAndLearningState()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var store = new EfWordCaptureStore(database.Factory);
        var wordId = await store.CreateWordWithProgressAsync("resilient", "resilient", "有韧性的", CancellationToken.None);
        await store.RecordCaptureAsync("resilient", "resilient", wordId, CancellationToken.None);

        var captured = await store.GetCapturedAsync(10, CancellationToken.None);

        var item = Assert.Single(captured);
        Assert.Equal("resilient", item.Word);
        Assert.Equal("有韧性的", item.DefinitionZh);
        Assert.True(item.IsLearning);
        Assert.Equal(1, item.CaptureCount);
    }

    [Fact]
    public async Task EnsureDueNow_MovesFutureDueToNow()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var store = new EfWordCaptureStore(database.Factory);
        var wordId = await store.CreateWordWithProgressAsync("resilient", "resilient", "有韧性的", CancellationToken.None);

        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var progress = await db.UserWordProgress.SingleAsync(x => x.WordId == wordId);
            progress.DueAt = DateTimeOffset.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        }

        await store.EnsureDueNowAsync(wordId, CancellationToken.None);

        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var progress = await db.UserWordProgress.SingleAsync(x => x.WordId == wordId);
            Assert.True(progress.DueAt <= DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task CapturedWord_AppearsInDueReviews()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var store = new EfWordCaptureStore(database.Factory);
        var wordId = await store.CreateWordWithProgressAsync("momentum", "momentum", "势头", CancellationToken.None);
        await store.RecordCaptureAsync("momentum", "momentum", wordId, CancellationToken.None);

        var learningStore = new EfLearningStore(database.Factory);
        var due = await learningStore.GetDueReviewsAsync(DateTimeOffset.UtcNow, 10, CancellationToken.None);

        Assert.Contains(due, x => x.Card.Id == wordId);
    }
}
