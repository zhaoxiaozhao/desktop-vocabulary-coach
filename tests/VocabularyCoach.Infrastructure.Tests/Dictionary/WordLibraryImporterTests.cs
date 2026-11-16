using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Infrastructure.Dictionary;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Tests.Support;

namespace VocabularyCoach.Infrastructure.Tests.Dictionary;

public class WordLibraryImporterTests
{
    private const string TwoWordsJson = """
        {
          "version": 1,
          "words": [
            {
              "word": "Apple",
              "phonetic": "ˈæpl",
              "difficulty": 1,
              "frequencyRank": 500,
              "definitions": [
                { "partOfSpeech": "n.", "definitionZh": "苹果" },
                { "partOfSpeech": "n.", "definitionZh": "苹果树" }
              ]
            },
            {
              "word": "resilient",
              "phonetic": "rɪˈzɪliənt",
              "difficulty": 3,
              "definitions": [
                { "partOfSpeech": "adj.", "definitionZh": "有韧性的；能迅速恢复的" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task ImportAsync_AddsWordsAndDefinitions()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var importer = CreateImporter(database);

        var result = await importer.ImportAsync(Json(TwoWordsJson), WordImportFormat.Json);

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Failed);

        await using var db = await database.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Words.CountAsync());

        var apple = await db.Words.Include(w => w.Definitions).SingleAsync(w => w.NormalizedWord == "apple");
        Assert.Equal("Apple", apple.Word);
        Assert.Equal("ˈæpl", apple.Phonetic);
        Assert.Equal(2, apple.Definitions.Count);
        Assert.Equal(["苹果", "苹果树"], apple.Definitions.OrderBy(d => d.SortOrder).Select(d => d.DefinitionZh));
    }

    [Fact]
    public async Task ImportAsync_IsIdempotent()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var importer = CreateImporter(database);

        await importer.ImportAsync(Json(TwoWordsJson), WordImportFormat.Json);
        var second = await importer.ImportAsync(Json(TwoWordsJson), WordImportFormat.Json);

        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Skipped);

        await using var db = await database.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Words.CountAsync());
        Assert.Equal(3, await db.WordDefinitions.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_UpdatesDefinitions_WithoutTouchingUserProgress()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var importer = CreateImporter(database);

        await importer.ImportAsync(Json(TwoWordsJson), WordImportFormat.Json);

        int wordId;
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var apple = await db.Words.SingleAsync(w => w.NormalizedWord == "apple");
            wordId = apple.Id;

            db.UserWordProgress.Add(new UserWordProgressRecord
            {
                WordId = wordId,
                Repetition = 4,
                IntervalDays = 12,
                DueAt = DateTimeOffset.UtcNow.AddDays(12),
                LastReviewedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        const string updatedJson = """
            {
              "words": [
                {
                  "word": "apple",
                  "phonetic": "ˈæpl",
                  "difficulty": 2,
                  "frequencyRank": 450,
                  "definitions": [
                    { "partOfSpeech": "n.", "definitionZh": "苹果；苹果树" }
                  ]
                }
              ]
            }
            """;

        var result = await importer.ImportAsync(Json(updatedJson), WordImportFormat.Json);

        Assert.Equal(1, result.Updated);

        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var apple = await db.Words.Include(w => w.Definitions).SingleAsync(w => w.Id == wordId);
            Assert.Equal(2, apple.Difficulty);
            Assert.Equal(450, apple.FrequencyRank);
            Assert.Single(apple.Definitions);
            Assert.Equal("苹果；苹果树", apple.Definitions[0].DefinitionZh);

            var progress = await db.UserWordProgress.SingleAsync(p => p.WordId == wordId);
            Assert.Equal(4, progress.Repetition);
            Assert.Equal(12, progress.IntervalDays);
        }
    }

    [Fact]
    public async Task ImportAsync_ReportsInvalidEntries_AndStillImportsValidOnes()
    {
        const string json = """
            {
              "words": [
                { "word": "", "definitions": [ { "definitionZh": "空单词" } ] },
                { "word": "useful", "definitions": [ { "definitionZh": "有用的" } ] },
                { "word": "broken", "definitions": [] }
              ]
            }
            """;

        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var importer = CreateImporter(database);

        var result = await importer.ImportAsync(Json(json), WordImportFormat.Json);

        Assert.Equal(1, result.Added);
        Assert.Equal(2, result.Failed);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ImportAsync_CsvGroupsRowsIntoMultipleDefinitions()
    {
        const string csv = """
            word,phonetic,difficulty,frequencyRank,partOfSpeech,definitionZh,exampleEn
            apple,ˈæpl,1,500,n.,苹果,"An apple a day, keeps the doctor away."
            apple,ˈæpl,1,500,n.,苹果树,
            """;

        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var importer = CreateImporter(database);

        var result = await importer.ImportAsync(Json(csv), WordImportFormat.Csv);

        Assert.Equal(1, result.Added);

        await using var db = await database.Factory.CreateDbContextAsync();
        var apple = await db.Words.Include(w => w.Definitions).SingleAsync(w => w.NormalizedWord == "apple");
        Assert.Equal(2, apple.Definitions.Count);
        Assert.Equal("An apple a day, keeps the doctor away.", apple.Definitions[0].ExampleEn);
    }

    [Fact]
    public async Task ImportAsync_RejectsMalformedCsvWithoutRequiredColumns()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();
        var importer = CreateImporter(database);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => importer.ImportAsync(Json("word,phonetic\napple,ˈæpl\n"), WordImportFormat.Csv));

        await using var db = await database.Factory.CreateDbContextAsync();
        Assert.Equal(0, await db.Words.CountAsync());
    }

    private static WordLibraryImporter CreateImporter(SqliteTestDatabase database) =>
        new(database.Factory, NullLogger<WordLibraryImporter>.Instance);

    private static MemoryStream Json(string content) => new(Encoding.UTF8.GetBytes(content));
}
