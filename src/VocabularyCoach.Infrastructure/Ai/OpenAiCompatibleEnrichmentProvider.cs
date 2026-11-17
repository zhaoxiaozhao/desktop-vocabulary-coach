using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible Chat Completions 实现。适配 OpenAI、Anthropic（兼容层）、Gemini、
/// DeepSeek、Moonshot、Qwen、Zhipu、SiliconFlow 等提供兼容接口的服务。
/// </summary>
public sealed class OpenAiCompatibleEnrichmentProvider : IVocabularyEnrichmentProvider
{
    private const string SystemPrompt =
        "You are an English vocabulary tutor for Chinese learners. " +
        "Return ONLY a JSON object with keys: exampleEn (string), exampleZh (string), " +
        "explanationZh (concise Chinese explanation, string), confusableNote (Chinese note about easily confused words, string or null). " +
        "Do not use markdown fences. Do not include any other keys.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiCompatibleEnrichmentProvider> _logger;

    public OpenAiCompatibleEnrichmentProvider(
        HttpClient httpClient,
        ILogger<OpenAiCompatibleEnrichmentProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Kind => "OpenAICompatible";

    public async Task<WordEnrichment?> EnrichAsync(
        AiEnrichmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl) || string.IsNullOrWhiteSpace(request.Model))
        {
            return null;
        }

        var url = $"{request.BaseUrl.TrimEnd('/')}/chat/completions";
        var payload = new
        {
            model = request.Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserPrompt(request) },
            },
            temperature = 0.4,
            // 推理模型（GLM-5.3 / DeepSeek R1 等）会先输出 reasoning_content，
            // 400 token 会导致 JSON 被截断，测试连接和卡片增强都会失败。
            max_tokens = 2000,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        }

        try
        {
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 只记录状态码，绝不记录请求头/Key。
                _logger.LogWarning(
                    "AI Provider 返回 {StatusCode}（{Provider} / {Model}）。",
                    (int)response.StatusCode,
                    request.BaseUrl,
                    request.Model);
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var finishReason = choices[0].TryGetProperty("finish_reason", out var finish)
                ? finish.GetString()
                : null;

            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "AI 输出被截断（finish_reason=length）：{BaseUrl} / {Model}。推理模型可能需要更大的 max_tokens。",
                    request.BaseUrl,
                    request.Model);
            }

            var content = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return AiResponseParser.Parse(content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "调用 AI Provider 失败（{BaseUrl}）。", request.BaseUrl);
            return null;
        }
    }

    private static string BuildUserPrompt(AiEnrichmentRequest request)
    {
        var definition = string.IsNullOrWhiteSpace(request.DefinitionZh)
            ? "(not provided)"
            : request.DefinitionZh;

        return $"Word: {request.Word}\nChinese definition: {definition}\nTarget difficulty (1-4): {request.Difficulty}";
    }
}
