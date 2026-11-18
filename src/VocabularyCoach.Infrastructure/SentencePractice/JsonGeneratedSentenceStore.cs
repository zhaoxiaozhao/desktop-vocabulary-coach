using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.SentencePractice;

namespace VocabularyCoach.Infrastructure.SentencePractice;

/// <summary>
/// 将后台 AI 生成的例句持久化到 %LocalAppData%\DesktopVocabularyCoach\sentences.generated.json。
/// 文件很小，整体载入内存，写入时用临时文件原子替换。
/// </summary>
public sealed class JsonGeneratedSentenceStore : IGeneratedSentenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly ILogger<JsonGeneratedSentenceStore> _logger;
    private readonly object _gate = new();
    private readonly List<GeneratedSentence> _entries;

    public JsonGeneratedSentenceStore(string path, ILogger<JsonGeneratedSentenceStore> logger)
    {
        _path = path;
        _logger = logger;
        _entries = Load(path);
    }

    public Task<IReadOnlyList<GeneratedSentence>> GetRandomAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (count <= 0)
        {
            return Task.FromResult<IReadOnlyList<GeneratedSentence>>([]);
        }

        lock (_gate)
        {
            var result = _entries
                .OrderBy(_ => Random.Shared.Next())
                .Take(count)
                .ToList();

            return Task.FromResult<IReadOnlyList<GeneratedSentence>>(result);
        }
    }

    public Task<GeneratedSentence?> GetRandomForWordAsync(
        int wordId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(
                _entries
                    .Where(x => x.WordId == wordId)
                    .OrderBy(_ => Random.Shared.Next())
                    .FirstOrDefault());
        }
    }

    public Task<int> CountSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_entries.Count(x => x.CreatedAt >= sinceUtc));
        }
    }

    public Task<int> CountForWordAsync(
        int wordId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_entries.Count(x => x.WordId == wordId));
        }
    }

    public Task AddAsync(
        IEnumerable<GeneratedSentence> sentences,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var existing = new HashSet<string>(
                _entries.Select(x => x.WordId + "\u001f" + x.Sentence),
                StringComparer.Ordinal);

            var added = 0;
            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence.Sentence)
                    || string.IsNullOrWhiteSpace(sentence.Answer)
                    || sentence.Options is not { Count: >= 4 }
                    || TranslationHasBlank(sentence.TranslationZh))
                {
                    continue;
                }

                if (existing.Add(sentence.WordId + "\u001f" + sentence.Sentence))
                {
                    _entries.Add(sentence);
                    added++;
                }
            }

            if (added > 0)
            {
                Save();
            }

            return Task.CompletedTask;
        }
    }

    private static bool TranslationHasBlank(string? translation) =>
        translation?.Contains('_') == true;

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                new FileModel { Version = 1, Sentences = _entries },
                JsonOptions);

            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存后台例句库失败：{Path}", _path);
        }
    }

    private List<GeneratedSentence> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var model = JsonSerializer.Deserialize<FileModel>(
                File.ReadAllText(path),
                JsonOptions);

            if (model?.Sentences is null)
            {
                return [];
            }

            return model.Sentences
                .Where(x => x is not null
                            && !string.IsNullOrWhiteSpace(x.Sentence)
                            && !string.IsNullOrWhiteSpace(x.Answer)
                            && x.Options is { Count: >= 4 }
                            && !TranslationHasBlank(x.TranslationZh))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取后台例句库失败：{Path}", path);
            return [];
        }
    }

    private sealed class FileModel
    {
        public int Version { get; set; }

        public List<GeneratedSentence> Sentences { get; set; } = [];
    }
}
