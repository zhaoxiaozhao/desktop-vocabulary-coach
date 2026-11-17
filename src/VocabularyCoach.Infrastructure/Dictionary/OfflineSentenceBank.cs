using System.Text.Json;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.SentencePractice;

namespace VocabularyCoach.Infrastructure.Dictionary;

/// <summary>
/// 读取 Seed/sentences.seed.json 的离线口语句库。
/// 文件缺失或条目无效时跳过，不影响应用启动。
/// </summary>
public sealed class OfflineSentenceBank : ISentenceBank
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IReadOnlyList<SentenceSeedEntry> _entries;

    public OfflineSentenceBank(string path, ILogger<OfflineSentenceBank> logger)
    {
        _entries = Load(path, logger);
    }

    public IReadOnlyList<SentenceSeedEntry> Entries => _entries;

    private static IReadOnlyList<SentenceSeedEntry> Load(
        string path,
        ILogger<OfflineSentenceBank> logger)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("未找到离线句库：{Path}", path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<SentenceSeedFile>(json, JsonOptions);
            if (document?.Sentences is null)
            {
                logger.LogWarning("离线句库为空或格式无效：{Path}", path);
                return [];
            }

            var entries = new List<SentenceSeedEntry>(document.Sentences.Count);
            var invalid = 0;

            foreach (var item in document.Sentences)
            {
                if (string.IsNullOrWhiteSpace(item.Word)
                    || string.IsNullOrWhiteSpace(item.Sentence)
                    || string.IsNullOrWhiteSpace(item.Answer)
                    || string.IsNullOrWhiteSpace(item.TranslationZh)
                    || item.Distractors is null
                    || item.Distractors.Count < 3
                    || CountBlanks(item.Sentence) != 1)
                {
                    invalid++;
                    continue;
                }

                entries.Add(new SentenceSeedEntry(
                    item.Word.Trim(),
                    item.Sentence.Trim(),
                    item.Answer.Trim(),
                    item.TranslationZh.Trim(),
                    string.IsNullOrWhiteSpace(item.Pattern) ? null : item.Pattern.Trim(),
                    string.IsNullOrWhiteSpace(item.GrammarNote) ? null : item.GrammarNote.Trim(),
                    item.Distractors.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                    item.AlternativeAnswers?
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList()
                    ?? []));
            }

            logger.LogInformation(
                "离线句库已加载：{Count} 条（跳过无效 {Invalid} 条）。",
                entries.Count,
                invalid);

            return entries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取离线句库失败：{Path}", path);
            return [];
        }
    }

    private static int CountBlanks(string sentence)
    {
        var count = 0;
        var index = 0;
        while ((index = sentence.IndexOf("____", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 4;
        }

        return count;
    }

    private sealed class SentenceSeedFile
    {
        public int Version { get; set; }

        public List<SentenceSeedItem>? Sentences { get; set; }
    }

    private sealed class SentenceSeedItem
    {
        public string? Word { get; set; }

        public string? Sentence { get; set; }

        public string? Answer { get; set; }

        public string? TranslationZh { get; set; }

        public string? Pattern { get; set; }

        public string? GrammarNote { get; set; }

        public List<string>? Distractors { get; set; }

        public List<string>? AlternativeAnswers { get; set; }
    }
}
