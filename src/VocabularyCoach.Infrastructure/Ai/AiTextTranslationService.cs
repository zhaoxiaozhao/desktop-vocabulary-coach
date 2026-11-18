using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>
/// 使用当前 AI 连接把中文翻译成英文；支持手动连接与 Auto 回退。
/// 只发送调用方传入的选中文本，不记录文本内容。
/// </summary>
public sealed class AiTextTranslationService : ITextTranslationService
{
    private const int MaxInputCharacters = 4000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private const string SystemPrompt = """
        You are a professional Chinese-to-English translator for everyday and business writing.
        Translate the user's Chinese text into natural, idiomatic English.
        Preserve names, numbers, URLs and formatting.
        Output ONLY the English translation, with no quotes, labels, or explanation.
        If the input is already English, return it unchanged.
        """;

    private readonly ISettingsService _settings;
    private readonly IAiConnectionResolver _resolver;
    private readonly ILogger<AiTextTranslationService> _logger;
    private readonly HttpClient _httpClient;

    public AiTextTranslationService(
        ISettingsService settings,
        IAiConnectionResolver resolver,
        ILogger<AiTextTranslationService> logger)
    {
        _settings = settings;
        _resolver = resolver;
        _logger = logger;
        _httpClient = new HttpClient { Timeout = RequestTimeout };
    }

    public async Task<TextTranslationResult> TranslateAsync(
        string chineseText,
        CancellationToken cancellationToken = default)
    {
        var text = chineseText?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return TextTranslationResult.Fail("没有可翻译的文字。");
        }

        if (text.Length > MaxInputCharacters)
        {
            return TextTranslationResult.Fail(
                $"选中的文字太长（{text.Length} 字），请控制在 {MaxInputCharacters} 字以内。");
        }

        var settings = _settings.Current;
        if (!settings.AiEnabled)
        {
            return TextTranslationResult.Fail("AI 已关闭，请先在设置中启用 AI 并配置连接。");
        }

        var candidates = await _resolver
            .GetCandidatesAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return TextTranslationResult.Fail("没有可用的 AI 连接，请先在设置中配置。");
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = candidate.Connection;

            if (connection.Preset == AiPreset.Mock
                || string.IsNullOrWhiteSpace(connection.BaseUrl)
                || string.IsNullOrWhiteSpace(connection.Model))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.ApiKey))
            {
                _logger.LogDebug("连接 {Name} 未配置 API Key，跳过翻译。", connection.Name);
                continue;
            }

            var url = $"{connection.BaseUrl.TrimEnd('/')}/chat/completions";
            var translated = await TranslateWithConnectionAsync(
                    url,
                    connection.Model,
                    candidate.ApiKey,
                    text,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(translated))
            {
                _logger.LogInformation(
                    "中文转英文成功（连接 {Name}，模型 {Model}，{Chars} 字）。",
                    connection.Name,
                    connection.Model,
                    text.Length);
                return TextTranslationResult.Ok(translated);
            }

            _logger.LogWarning("连接 {Name} 未返回有效译文。", connection.Name);

            if (!settings.AiAutoMode)
            {
                break;
            }
        }

        return TextTranslationResult.Fail("翻译失败：请检查 AI 连接、模型与网络后重试。");
    }

    private async Task<string?> TranslateWithConnectionAsync(
        string url,
        string model,
        string apiKey,
        string text,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = text },
            },
            temperature = 0.2,
            max_tokens = 2000,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AI 翻译请求失败：{StatusCode}（{Url} / {Model}）。",
                    (int)response.StatusCode,
                    url,
                    model);
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = ExtractContent(document);
            var cleaned = TextTranslationText.Clean(content);
            return cleaned.Length == 0 ? null : cleaned;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI 翻译超时（{Seconds} 秒）。", RequestTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 翻译失败。");
            return null;
        }
    }

    private static string? ExtractContent(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var messageElement)
            || !messageElement.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return contentElement.GetString();
    }
}
