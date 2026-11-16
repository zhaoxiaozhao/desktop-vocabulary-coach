using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Dictionary;

/// <summary>
/// 可重复执行的词库导入器（JSON/CSV）。按 NormalizedWord 去重，
/// 只更新词条与释义，不触碰 UserWordProgress / ReviewLogs。
/// </summary>
public sealed class WordLibraryImporter : IWordLibraryImporter
{
    private const int MaxDefinitionsPerWord = 5;
    private const int MaxReportedErrors = 100;
    private const int BatchSize = 250;
    private const int QueryChunkSize = 400;

    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;
    private readonly ILogger<WordLibraryImporter> _logger;

    public WordLibraryImporter(
        IDbContextFactory<VocabularyCoachDbContext> dbContextFactory,
        ILogger<WordLibraryImporter> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<WordImportResult> ImportAsync(
        Stream stream,
        WordImportFormat format,
        CancellationToken cancellationToken = default)
    {
        var parsed = format switch
        {
            WordImportFormat.Json => await ReadJsonAsync(stream, cancellationToken).ConfigureAwait(false),
            WordImportFormat.Csv => await ReadCsvAsync(stream, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        var errors = parsed.Errors.ToList();
        var words = MergeWords(parsed.Words, errors);

        if (words.Count == 0)
        {
            return new WordImportResult(0, 0, 0, errors.Count, errors.Take(MaxReportedErrors).ToList());
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var processed = 0;
        var now = DateTimeOffset.UtcNow;

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var existingWords = await LoadExistingWordsAsync(db, words.Select(w => w.NormalizedWord), cancellationToken)
            .ConfigureAwait(false);

        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!existingWords.TryGetValue(word.NormalizedWord, out var record))
            {
                record = new WordRecord
                {
                    Word = word.Word,
                    NormalizedWord = word.NormalizedWord,
                    Phonetic = word.Phonetic,
                    Difficulty = word.Difficulty,
                    FrequencyRank = word.FrequencyRank,
                    Source = word.Source,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Definitions = BuildDefinitions(word.Definitions),
                };

                db.Words.Add(record);
                existingWords[word.NormalizedWord] = record;
                added++;
            }
            else if (ApplyUpdate(record, word, now))
            {
                updated++;
            }
            else
            {
                skipped++;
            }

            processed++;
            if (processed % BatchSize == 0)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                db.ChangeTracker.Clear();
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var result = new WordImportResult(added, updated, skipped, errors.Count, errors.Take(MaxReportedErrors).ToList());
        _logger.LogInformation("词库导入完成：{Summary}", result);

        foreach (var error in errors.Take(10))
        {
            _logger.LogWarning("词库导入跳过条目 {Word}：{Message}", error.Word, error.Message);
        }

        return result;
    }

    private static bool ApplyUpdate(WordRecord record, ImportWord incoming, DateTimeOffset now)
    {
        var metadataChanged =
            record.Word != incoming.Word ||
            record.Phonetic != incoming.Phonetic ||
            record.Difficulty != incoming.Difficulty ||
            record.FrequencyRank != incoming.FrequencyRank;

        var existingDefinitions = record.Definitions
            .OrderBy(d => d.SortOrder)
            .Select(DefinitionKey)
            .ToList();
        var incomingDefinitions = incoming.Definitions.Select(DefinitionKey).ToList();

        if (!metadataChanged && existingDefinitions.SequenceEqual(incomingDefinitions))
        {
            return false;
        }

        record.Word = incoming.Word;
        record.Phonetic = incoming.Phonetic;
        record.Difficulty = incoming.Difficulty;
        record.FrequencyRank = incoming.FrequencyRank;
        record.Source = incoming.Source;
        record.UpdatedAt = now;

        if (!existingDefinitions.SequenceEqual(incomingDefinitions))
        {
            record.Definitions.Clear();
            record.Definitions.AddRange(BuildDefinitions(incoming.Definitions));
        }

        return true;
    }

    private static string DefinitionKey(WordDefinitionRecord definition) =>
        string.Join('\u001f',
            definition.PartOfSpeech ?? string.Empty,
            definition.DefinitionZh,
            definition.DefinitionEn ?? string.Empty,
            definition.ExampleEn ?? string.Empty,
            definition.ExampleZh ?? string.Empty);

    private static string DefinitionKey(ImportDefinition definition) =>
        string.Join('\u001f',
            definition.PartOfSpeech ?? string.Empty,
            definition.DefinitionZh,
            definition.DefinitionEn ?? string.Empty,
            definition.ExampleEn ?? string.Empty,
            definition.ExampleZh ?? string.Empty);

    private static List<WordDefinitionRecord> BuildDefinitions(IReadOnlyList<ImportDefinition> definitions) =>
        definitions.Select((definition, index) => new WordDefinitionRecord
        {
            PartOfSpeech = definition.PartOfSpeech,
            DefinitionZh = definition.DefinitionZh,
            DefinitionEn = definition.DefinitionEn,
            ExampleEn = definition.ExampleEn,
            ExampleZh = definition.ExampleZh,
            SortOrder = index,
        }).ToList();

    private static async Task<Dictionary<string, WordRecord>> LoadExistingWordsAsync(
        VocabularyCoachDbContext db,
        IEnumerable<string> normalizedWords,
        CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, WordRecord>(StringComparer.Ordinal);
        var keys = normalizedWords.Distinct(StringComparer.Ordinal).ToList();

        foreach (var chunk in keys.Chunk(QueryChunkSize))
        {
            var records = await db.Words
                .Include(x => x.Definitions)
                .Where(x => chunk.Contains(x.NormalizedWord))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var record in records)
            {
                lookup[record.NormalizedWord] = record;
            }
        }

        return lookup;
    }

    private static List<ImportWord> MergeWords(List<ImportWord> words, List<WordImportError> errors)
    {
        var merged = new List<ImportWord>(words.Count);
        var index = new Dictionary<string, ImportWord>(StringComparer.Ordinal);

        foreach (var word in words)
        {
            if (!index.TryGetValue(word.NormalizedWord, out var target))
            {
                target = word with { Definitions = [] };
                index[word.NormalizedWord] = target;
                merged.Add(target);
            }

            foreach (var definition in word.Definitions)
            {
                if (target.Definitions.Count >= MaxDefinitionsPerWord)
                {
                    break;
                }

                var key = DefinitionKey(definition);
                if (target.Definitions.Any(existing => DefinitionKey(existing) == key))
                {
                    continue;
                }

                target.Definitions.Add(definition);
            }

            if (target.Definitions.Count == 0)
            {
                errors.Add(new WordImportError(word.Word, "缺少有效的中文释义。"));
            }
        }

        return merged.Where(w => w.Definitions.Count > 0).ToList();
    }

    internal static string Normalize(string word) => word.Trim().ToLowerInvariant();

    private static async Task<ParsedWords> ReadJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        var errors = new List<WordImportError>();
        var words = new List<ImportWord>();

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        JsonElement wordArray;
        if (root.ValueKind == JsonValueKind.Array)
        {
            wordArray = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "words", out var wordsElement))
        {
            wordArray = wordsElement;
        }
        else
        {
            throw new InvalidDataException("JSON 词库格式错误：应为单词数组，或包含 words 数组的对象。");
        }

        if (wordArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("JSON 词库格式错误：words 必须是数组。");
        }

        foreach (var item in wordArray.EnumerateArray())
        {
            var rawWord = GetString(item, "word");
            var normalized = Normalize(rawWord ?? string.Empty);
            if (normalized.Length is 0 or > 64)
            {
                errors.Add(new WordImportError(rawWord ?? "(空)", "单词为空或长度超过 64。"));
                continue;
            }

            var definitions = new List<ImportDefinition>();
            if (TryGetProperty(item, "definitions", out var definitionArray) &&
                definitionArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var definitionElement in definitionArray.EnumerateArray())
                {
                    var definition = ReadDefinition(
                        GetString(definitionElement, "definitionZh"),
                        GetString(definitionElement, "partOfSpeech"),
                        GetString(definitionElement, "definitionEn"),
                        GetString(definitionElement, "exampleEn"),
                        GetString(definitionElement, "exampleZh"));

                    if (definition is not null)
                    {
                        definitions.Add(definition);
                    }
                }
            }
            else
            {
                var definition = ReadDefinition(
                    GetString(item, "definitionZh"),
                    GetString(item, "partOfSpeech"),
                    GetString(item, "definitionEn"),
                    GetString(item, "exampleEn"),
                    GetString(item, "exampleZh"));

                if (definition is not null)
                {
                    definitions.Add(definition);
                }
            }

            if (definitions.Count == 0)
            {
                errors.Add(new WordImportError(rawWord!, "缺少有效的中文释义。"));
                continue;
            }

            words.Add(new ImportWord(
                rawWord!.Trim(),
                normalized,
                NullIfEmpty(GetString(item, "phonetic")),
                ClampDifficulty(GetInt32(item, "difficulty")),
                GetNullableInt32(item, "frequencyRank"),
                NullIfEmpty(GetString(item, "source")) ?? "Imported",
                definitions));
        }

        return new ParsedWords(words, errors);
    }

    private static async Task<ParsedWords> ReadCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        var errors = new List<WordImportError>();
        var words = new List<ImportWord>();

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("CSV 词库为空。");

        var headers = SplitCsvLine(headerLine)
            .Select((name, index) => (Name: name.Trim().ToLowerInvariant(), Index: index))
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.Ordinal);

        foreach (var required in new[] { "word", "definitionzh" })
        {
            if (!headers.ContainsKey(required))
            {
                throw new InvalidDataException($"CSV 缺少必需列：{required}。");
            }
        }

        var lineNumber = 1;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitCsvLine(line);
            string Cell(string name) =>
                headers.TryGetValue(name, out var index) && index < cells.Count ? cells[index].Trim() : string.Empty;

            var rawWord = Cell("word");
            var normalized = Normalize(rawWord);
            if (normalized.Length is 0 or > 64)
            {
                errors.Add(new WordImportError(rawWord, $"第 {lineNumber} 行单词为空或过长。"));
                continue;
            }

            var definition = ReadDefinition(
                Cell("definitionzh"),
                Cell("partofspeech"),
                Cell("definitionen"),
                Cell("exampleen"),
                Cell("examplezh"));

            if (definition is null)
            {
                errors.Add(new WordImportError(rawWord, $"第 {lineNumber} 行缺少中文释义。"));
                continue;
            }

            words.Add(new ImportWord(
                rawWord,
                normalized,
                NullIfEmpty(Cell("phonetic")),
                ClampDifficulty(ParseInt(Cell("difficulty"))),
                ParseNullableInt(Cell("frequencyrank")),
                NullIfEmpty(Cell("source")) ?? "Imported",
                [definition]));
        }

        return new ParsedWords(words, errors);
    }

    /// <summary>解析一行 CSV，支持双引号包裹、转义引号和字段内逗号。</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    cells.Add(builder.ToString());
                    builder.Clear();
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        cells.Add(builder.ToString());
        return cells;
    }

    private static ImportDefinition? ReadDefinition(
        string? definitionZh,
        string? partOfSpeech,
        string? definitionEn,
        string? exampleEn,
        string? exampleZh)
    {
        if (string.IsNullOrWhiteSpace(definitionZh))
        {
            return null;
        }

        return new ImportDefinition(
            NullIfEmpty(partOfSpeech),
            definitionZh.Trim(),
            NullIfEmpty(definitionEn),
            NullIfEmpty(exampleEn),
            NullIfEmpty(exampleZh));
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

    private static int GetInt32(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 2;

    private static int? GetNullableInt32(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var number) ? number : null;
    }

    private static int ClampDifficulty(int value) => Math.Clamp(value, 1, 4);

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int ParseInt(string value) => ParseNullableInt(value) ?? 2;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedWords(List<ImportWord> Words, List<WordImportError> Errors);

    private sealed record ImportWord(
        string Word,
        string NormalizedWord,
        string? Phonetic,
        int Difficulty,
        int? FrequencyRank,
        string Source,
        List<ImportDefinition> Definitions);

    private sealed record ImportDefinition(
        string? PartOfSpeech,
        string DefinitionZh,
        string? DefinitionEn,
        string? ExampleEn,
        string? ExampleZh);
}
