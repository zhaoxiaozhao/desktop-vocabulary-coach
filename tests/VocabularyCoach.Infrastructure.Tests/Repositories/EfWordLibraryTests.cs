using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Infrastructure.Dictionary;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Repositories;
using VocabularyCoach.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace VocabularyCoach.Infrastructure.Tests.Repositories;

public class EfWordLibraryTests
{
    [Fact]
    public async Task FindByWordAsync_IsCaseInsensitive_AndOrdersDefinitions()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var importer = new WordLibraryImporter(database.Factory, NullLogger<WordLibraryImporter>.Instance);
        var json = """
            {
              "words": [
                {
                  "word": "apple",
                  "phonetic": "ˈæpl",
                  "difficulty": 1,
                  "definitions": [
                    { "partOfSpeech": "n.", "definitionZh": "苹果" },
                    { "partOfSpeech": "n.", "definitionZh": "苹果树" }
                  ]
                }
              ]
            }
            """;
        await using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
        {
            await importer.ImportAsync(stream, WordImportFormat.Json);
        }

        var library = new EfWordLibrary(database.Factory);
        var card = await library.FindByWordAsync("  APPLE ");

        Assert.NotNull(card);
        Assert.Equal("apple", card!.Word);
        Assert.Equal("/ˈæpl/", card.DisplayPhonetic);
        Assert.Equal(["苹果", "苹果树"], card.Definitions.Select(d => d.DefinitionZh));
        Assert.Equal(1, await library.GetWordCountAsync());
    }

    [Fact]
    public async Task FindByWordAsync_ReturnsNullWhenMissing()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var library = new EfWordLibrary(database.Factory);

        Assert.Null(await library.FindByWordAsync("not-in-dictionary"));
        Assert.Null(await library.FindByWordAsync("   "));
    }

    [Fact]
    public async Task GetNextNewWordAsync_RespectsDifficultyAndSkipsLearnedWords()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var easy = new WordRecord { Word = "easy", NormalizedWord = "easy", Difficulty = 1, FrequencyRank = 100 };
            var hard = new WordRecord { Word = "hard", NormalizedWord = "hard", Difficulty = 3, FrequencyRank = 200 };
            db.Words.AddRange(easy, hard);
            await db.SaveChangesAsync();

            db.UserWordProgress.Add(new UserWordProgressRecord
            {
                WordId = easy.Id,
                Repetition = 2,
                IntervalDays = 5,
                DueAt = DateTimeOffset.UtcNow.AddDays(5),
                LastReviewedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var library = new EfWordLibrary(database.Factory);

        var withinLimit = await library.GetNextNewWordAsync(maxDifficulty: 2);
        Assert.Null(withinLimit);

        var card = await library.GetNextNewWordAsync(maxDifficulty: 3);
        Assert.NotNull(card);
        Assert.Equal("hard", card!.Word);
    }

    [Fact]
    public async Task GetDistractorDefinitionsAsync_ExcludesWordAndReturnsDistinctDefinitions()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var targetId = await AddWordWithDefinitionAsync(database, "resilient", "有韧性的");
        await AddWordWithDefinitionAsync(database, "ambiguous", "模棱两可的");
        await AddWordWithDefinitionAsync(database, "momentum", "势头");
        await AddWordWithDefinitionAsync(database, "gratitude", "感激");

        var library = new EfWordLibrary(database.Factory);
        var definitions = await library.GetDistractorDefinitionsAsync(targetId, 3, 1, CancellationToken.None);

        Assert.Equal(3, definitions.Count);
        Assert.DoesNotContain("有韧性的", definitions);
        Assert.Equal(3, definitions.Distinct().Count());
    }

    [Fact]
    public async Task GetDistractorWordsAsync_ExcludesTargetAndWordsWithSameDefinition()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var targetId = await AddWordWithDefinitionAsync(database, "resilient", "有韧性的");
        await AddWordWithDefinitionAsync(database, "resilience", "有韧性的");
        await AddWordWithDefinitionAsync(database, "ambiguous", "模棱两可的");
        await AddWordWithDefinitionAsync(database, "momentum", "势头");
        await AddWordWithDefinitionAsync(database, "gratitude", "感激");

        var library = new EfWordLibrary(database.Factory);
        var words = await library.GetDistractorWordsAsync(
            targetId,
            "有韧性的",
            "resilient".Length,
            3,
            1,
            CancellationToken.None);

        Assert.Equal(3, words.Count);
        Assert.DoesNotContain("resilient", words);
        Assert.DoesNotContain("resilience", words);
        Assert.Equal(3, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static async Task<int> AddWordWithDefinitionAsync(
        SqliteTestDatabase database,
        string word,
        string definitionZh)
    {
        await using var db = await database.Factory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        var record = new WordRecord
        {
            Word = word,
            NormalizedWord = word,
            Difficulty = 1,
            FrequencyRank = 100,
            CreatedAt = now,
            UpdatedAt = now,
            Definitions = [new WordDefinitionRecord { DefinitionZh = definitionZh }],
        };
        db.Words.Add(record);
        await db.SaveChangesAsync();
        return record.Id;
    }
}
