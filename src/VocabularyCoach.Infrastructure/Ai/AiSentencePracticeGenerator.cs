using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.SentencePractice;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>
/// 使用 OpenAI-compatible Chat Completions 一次生成一组句子填空练习。
/// AI 关闭、Mock、未配置或返回格式无效时返回空列表，由离线句库兜底。
/// </summary>
public sealed class AiSentencePracticeGenerator : ISentencePracticeGenerator
{
    private const int MaxWords = 10;

    private const string SystemPrompt = """
        You are an English tutor for Chinese learners. Create spoken-English fill-in-the-blank exercises.
        Return ONLY a JSON array. Each element must have these keys:
        - word: the target dictionary word
        - sentence: a natural spoken sentence with exactly one blank written as ____
        - answer: the word/phrase that fills the blank, grammatically correct
        - translationZh: natural Chinese translation
        - pattern: the fixed expression or grammar point, e.g. "look forward to doing"
        - grammarNote: concise Chinese explanation (<= 60 Chinese characters)
        - distractors: exactly 3 alternative answers that fit the same grammatical slot but are wrong
        Rules: sentence 6-16 words; no markdown; no extra keys; distractors must not be synonyms of answer.
        """;

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ILogger<AiSentencePracticeGenerator> _logger;
    private readonly HttpClient _httpClient;

    public AiSentencePracticeGenerator(
        ISettingsService settings,
        ISecretStore secrets,
        ILogger<AiSentencePracticeGenerator> logger)
    {
        _settings = settings;
        _secrets = secrets;
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<IReadOnlyList<SentencePracticeItem>> GenerateAsync(
        IReadOnlyList<WordCard> cards,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        if (!settings.AiEnabled
            || settings.AiPreset == AiPreset.Mock
            || string.IsNullOrWhiteSpace(settings.AiBaseUrl)
            || string.IsNullOrWhiteSpace(settings.AiModel)
            || cards.Count == 0)
        {
            return [];
        }

        var selected = cards.Take(MaxWords).ToList();
        var apiKey = await _secrets
            .TryReadAsync(cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        var url = $"{settings.AiBaseUrl.TrimEnd('/')}/chat/completions";
        var payload = new
        {
            model = settings.AiModel,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserPrompt(selected) },
            },
            temperature = 0.7,
            max_tokens = 2500,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AI 句子生成请求失败：{StatusCode}，回退离线句库。",
                    (int)response.StatusCode);
                return [];
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = ExtractContent(document);
            var items = ParseItems(content, selected);
            if (items.Count > 0)
            {
                _logger.LogInformation("AI 已生成 {Count} 条句子练习。", items.Count);
            }

            return items;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 句子生成失败，回退离线句库。");
            return [];
        }
    }

    private static string BuildUserPrompt(IReadOnlyList<WordCard> cards)
    {
        var lines = cards.Select((card, index) =>
        {
            var definition = card.PrimaryDefinition?.DefinitionZh ?? "(no definition)";
            return $"{index + 1}. {card.Word} — {definition} (difficulty {card.Difficulty})";
        });

        return "Generate one exercise for each word below:\n" + string.Join('\n', lines);
    }

    private static string? ExtractContent(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0];
        if (!message.TryGetProperty("message", out var messageElement)
            || !messageElement.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return contentElement.GetString();
    }

    private List<SentencePracticeItem> ParseItems(string? content, IReadOnlyList<WordCard> cards)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            _logger.LogWarning("AI 句子响应中没有 JSON 数组。");
            return [];
        }

        var cardByWord = cards
            .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var items = new List<SentencePracticeItem>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var word = GetString(element, "word");
                if (string.IsNullOrWhiteSpace(word)
                    || !cardByWord.TryGetValue(word.Trim(), out var card))
                {
                    continue;
                }

                var sentence = NormalizeSentence(GetString(element, "sentence"));
                var answer = GetString(element, "answer")?.Trim();
                var translation = GetString(element, "translationZh")?.Trim();
                var pattern = GetString(element, "pattern")?.Trim();
                var grammarNote = GetString(element, "grammarNote")?.Trim();
                var distractors = GetStrings(element, "distractors");

                if (sentence is null
                    || string.IsNullOrWhiteSpace(answer)
                    || string.IsNullOrWhiteSpace(translation)
                    || CountBlanks(sentence) != 1
                    || distractors.Count < 3)
                {
                    continue;
                }

                var options = new[] { answer }
                    .Concat(distractors)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .OrderBy(_ => Random.Shared.Next())
                    .ToList();

                if (options.Count < 4)
                {
                    continue;
                }

                items.Add(new SentencePracticeItem(
                    card.Id,
                    card.Word,
                    sentence,
                    answer,
                    [],
                    translation,
                    string.IsNullOrWhiteSpace(pattern) ? null : pattern,
                    string.IsNullOrWhiteSpace(grammarNote) ? null : grammarNote,
                    options,
                    FromAi: true));
            }

            return items;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AI 句子响应 JSON 解析失败。");
            return [];
        }
    }

    private static string? NormalizeSentence(string? sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return null;
        }

        var text = Regex.Replace(sentence.Trim(), "_{2,}", "____");
        return text;
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

    private static string? GetString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetStrings(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return property.Value
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => x.Length > 0)
                .ToList();
        }

        return [];
    }
}
