using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Infrastructure.Ai;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Tests.Support;

namespace VocabularyCoach.Infrastructure.Tests.Ai;

public class EfAiCacheStoreTests
{
    [Fact]
    public async Task SaveAndGet_RoundTripsEnrichment()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database);

        var store = new EfAiCacheStore(database.Factory);
        var enrichment = new WordEnrichment("A.", "甲。", "简释", "辨析");

        await store.SaveAsync(wordId, "Mock", "mock", enrichment, CancellationToken.None);

        var loaded = await store.GetAsync(wordId, "Mock", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("A.", loaded!.ExampleEn);
        Assert.Equal("甲。", loaded.ExampleZh);
        Assert.Equal("简释", loaded.ExplanationZh);
        Assert.Equal("辨析", loaded.ConfusableNote);
    }

    [Fact]
    public async Task SaveTwice_UpdatesExistingRow()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database);

        var store = new EfAiCacheStore(database.Factory);
        await store.SaveAsync(wordId, "Mock", "m1", new WordEnrichment("A.", "甲。", null, null), CancellationToken.None);
        await store.SaveAsync(wordId, "Mock", "m2", new WordEnrichment("B.", "乙。", "简释", null), CancellationToken.None);

        var loaded = await store.GetAsync(wordId, "Mock", CancellationToken.None);

        Assert.Equal("B.", loaded!.ExampleEn);
        await using var db = await database.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.AiGenerationCache.CountAsync());
        Assert.Equal("m2", await db.AiGenerationCache.Select(x => x.Model).SingleAsync());
    }

    [Fact]
    public async Task Get_ReturnsNullForUnknownProvider()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var wordId = await AddWordAsync(database);

        var store = new EfAiCacheStore(database.Factory);

        Assert.Null(await store.GetAsync(wordId, "OpenAi", CancellationToken.None));
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
}
