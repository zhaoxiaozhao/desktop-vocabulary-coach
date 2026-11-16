using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// 从 ECDICT（MIT 许可）CSV 中筛选常用词，生成应用内置种子词库。
// 用法：dotnet run --project tools/SeedBuilder -- <ecdict.csv> <输出.json> [词条数]
// 说明：ECDICT 的 CSV 为 UTF-8，字段可能包含逗号、引号和换行。

var inputPath = args.Length > 0 ? args[0] : throw new ArgumentException("缺少 ECDICT CSV 路径");
var outputPath = args.Length > 1 ? args[1] : Path.Combine("data", "seed", "words.seed.json");
var maxWords = args.Length > 2 ? int.Parse(args[2]) : 4000;
var seedVersion = args.Length > 3 ? int.Parse(args[3]) : 1;

var wordPattern = new Regex("^[a-z][a-z'-]*[a-z]$", RegexOptions.Compiled);
var partOfSpeechPattern = new Regex(
    "^(?<pos>(?:[a-z]{1,6}\\.)+(?:\\s*[&/,]\\s*[a-z]{1,6}\\.)*)\\s*",
    RegexOptions.Compiled);

var commonTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "zk", "gk", "cet4", "cet6", "ky", "ielts", "toefl", "gre",
};

// 高频虚词不适合作为学习卡片，直接排除（可按需扩充）。
var stopWords = new HashSet<string>(StringComparer.Ordinal)
{
    "a", "an", "the", "and", "or", "but", "if", "of", "to", "in", "on", "at", "by", "for", "with",
    "from", "as", "be", "am", "is", "are", "was", "were", "been", "being", "do", "does", "did",
    "have", "has", "had", "he", "she", "it", "they", "we", "you", "i", "me", "him", "her", "them",
    "us", "my", "your", "his", "its", "our", "their", "this", "that", "these", "those", "there",
    "here", "what", "which", "who", "whom", "whose", "when", "where", "why", "how", "not", "no",
    "so", "than", "too", "very", "can", "could", "will", "would", "shall", "should", "may",
    "might", "must", "also", "just", "only", "then", "now", "up", "down", "out", "off", "over",
    "into", "about", "after", "before", "again", "any", "all", "some", "such", "own", "same",
    "both", "each", "few", "more", "most", "other", "nor", "per", "via", "yet", "upon", "onto",
    "myself", "yourself", "himself", "herself", "itself", "ourselves", "yourselves", "themselves",
    "oneself", "someone", "somebody", "something", "anyone", "anybody", "anything", "everyone",
    "everybody", "everything", "nobody", "nothing", "whoever", "whatever", "whichever", "whenever",
    "wherever", "however", "moreover", "therefore", "otherwise", "yes", "oh", "ah", "hello", "hi",
    "okay", "ok", "please", "thanks", "thank", "sorry", "wow", "ought", "let",
};

var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);

using (var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
{
    var header = ReadRow(reader) ?? throw new InvalidOperationException("CSV 为空");
    var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
    var rows = 0;

    while (ReadRow(reader) is { } row)
    {
        rows++;
        var candidate = TryBuildCandidate(row, index);
        if (candidate is null)
        {
            continue;
        }

        if (!candidates.TryGetValue(candidate.Word, out var existing) || candidate.Rank < existing.Rank)
        {
            candidates[candidate.Word] = candidate;
        }
    }

    Console.WriteLine($"读取 {rows} 行，候选 {candidates.Count} 个。");
}

var selected = candidates.Values
    .OrderBy(c => c.Rank)
    .ThenBy(c => c.Word, StringComparer.Ordinal)
    .Take(maxWords)
    .Select(c => c.ToSeedWord())
    .ToList();

var seed = new SeedFile
{
    Source = "ECDICT",
    SourceUrl = "https://github.com/skywind3000/ECDICT",
    License = "MIT",
    Version = seedVersion,
    GeneratedAt = DateTimeOffset.Now,
    WordCount = selected.Count,
    Words = selected,
};

var json = JsonSerializer.Serialize(seed, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
});

var fullOutputPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
await File.WriteAllTextAsync(fullOutputPath, json, new UTF8Encoding(false));

Console.WriteLine($"已写出 {selected.Count} 个单词到 {fullOutputPath}");
Console.WriteLine(
    $"难度分布：1={selected.Count(w => w.Difficulty == 1)}，2={selected.Count(w => w.Difficulty == 2)}，3={selected.Count(w => w.Difficulty == 3)}，4={selected.Count(w => w.Difficulty == 4)}");

Candidate? TryBuildCandidate(string[] row, Dictionary<string, int> index)
{
    string Get(string name) =>
        index.TryGetValue(name, out var i) && i < row.Length ? row[i].Trim() : string.Empty;

    var word = Get("word").ToLowerInvariant();
    if (word.Length is < 2 or > 24 || !wordPattern.IsMatch(word) || word.Contains('\'') || stopWords.Contains(word))
    {
        return null;
    }

    var translation = Get("translation");
    if (translation.Length == 0 || !translation.Any(IsChinese))
    {
        return null;
    }

    var definitions = ParseDefinitions(translation);
    if (definitions.Count == 0)
    {
        return null;
    }

    var bnc = ParseRank(Get("bnc"));
    var frq = ParseRank(Get("frq"));
    var rank = frq > 0 ? frq : bnc;

    // 跳过最高频的 300 个词：太基础，不适合作为每日新词。
    if (rank is > 0 and < 300)
    {
        return null;
    }

    var collins = ParseRank(Get("collins"));
    var oxford = Get("oxford") == "1";
    var tags = Get("tag").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var isCommon = oxford || collins > 0 || tags.Any(commonTags.Contains) || (rank > 0 && rank <= 8000);

    if (!isCommon)
    {
        return null;
    }

    if (rank <= 0)
    {
        rank = int.MaxValue;
    }

    var difficulty = rank switch
    {
        <= 2000 => 1,
        <= 6000 => 2,
        <= 12000 => 3,
        _ => 4,
    };

    return new Candidate(
        word,
        NormalizePhonetic(NullIfEmpty(Get("phonetic"))),
        rank == int.MaxValue ? null : rank,
        difficulty,
        definitions);
}

List<SeedDefinition> ParseDefinitions(string translation)
{
    var definitions = new List<SeedDefinition>();

    foreach (var rawLine in translation
                 .Replace("\\n", "\n", StringComparison.Ordinal)
                 .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        // 带 [计] [网络] 等领域标记的释义对通用学习是噪音，直接丢弃。
        if (rawLine.StartsWith('['))
        {
            continue;
        }

        var line = rawLine.Trim();
        if (line.Length == 0)
        {
            continue;
        }

        var partOfSpeech = string.Empty;
        var match = partOfSpeechPattern.Match(line);
        if (match.Success)
        {
            partOfSpeech = match.Groups["pos"].Value.Trim();
            line = line[match.Length..].Trim();
        }

        if (line.Length == 0)
        {
            continue;
        }

        definitions.Add(new SeedDefinition(NullIfEmpty(partOfSpeech), line, null));

        if (definitions.Count >= 2)
        {
            break;
        }
    }

    return definitions;
}

string? NormalizePhonetic(string? value)
{
    if (value is null)
    {
        return null;
    }

    var normalized = value
        .Replace('\u04D9', '\u0259')  // 西里尔 schwa → IPA schwa
        .Replace(":", "\u02D0")        // ECDICT 用冒号表示长音
        .Trim();

    return normalized.Length == 0 ? null : normalized;
}

int ParseRank(string value) =>
    int.TryParse(value, out var rank) && rank > 0 ? rank : 0;

string? NullIfEmpty(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;

bool IsChinese(char c) => c is >= '\u4e00' and <= '\u9fff';

string[]? ReadRow(TextReader reader)
{
    var fields = new List<string>();
    var field = new StringBuilder();
    var inQuotes = false;
    var hasContent = false;

    while (true)
    {
        var read = reader.Read();
        if (read < 0)
        {
            if (!hasContent && fields.Count == 0 && field.Length == 0)
            {
                return null;
            }

            fields.Add(field.ToString());
            return fields.ToArray();
        }

        hasContent = true;
        var c = (char)read;

        if (inQuotes)
        {
            if (c == '"')
            {
                if (reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                }
                else
                {
                    inQuotes = false;
                }
            }
            else
            {
                field.Append(c);
            }

            continue;
        }

        switch (c)
        {
            case '"':
                inQuotes = true;
                break;
            case ',':
                fields.Add(field.ToString());
                field.Clear();
                break;
            case '\r':
                break;
            case '\n':
                fields.Add(field.ToString());
                return fields.ToArray();
            default:
                field.Append(c);
                break;
        }
    }
}

internal sealed record Candidate(
    string Word,
    string? Phonetic,
    int? FrequencyRank,
    int Difficulty,
    List<SeedDefinition> Definitions)
{
    public int Rank => FrequencyRank ?? int.MaxValue;

    public SeedWord ToSeedWord() => new()
    {
        Word = Word,
        Phonetic = Phonetic,
        Difficulty = Difficulty,
        FrequencyRank = FrequencyRank,
        Definitions = Definitions,
    };
}

internal sealed class SeedFile
{
    public required string Source { get; init; }
    public required string SourceUrl { get; init; }
    public required string License { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required int WordCount { get; init; }
    public required List<SeedWord> Words { get; init; }
}

internal sealed class SeedWord
{
    public required string Word { get; init; }
    public string? Phonetic { get; init; }
    public int Difficulty { get; init; }
    public int? FrequencyRank { get; init; }
    public required List<SeedDefinition> Definitions { get; init; }
}

internal sealed record SeedDefinition(string? PartOfSpeech, string DefinitionZh, string? DefinitionEn);
