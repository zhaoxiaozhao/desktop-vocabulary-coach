using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Ai;

/// <summary>AI 生成内容缓存（AiGenerationCache 表）。</summary>
public interface IAiCacheStore
{
    Task<WordEnrichment?> GetAsync(int wordId, string provider, CancellationToken cancellationToken);

    Task SaveAsync(
        int wordId,
        string provider,
        string model,
        WordEnrichment enrichment,
        CancellationToken cancellationToken);
}

/// <summary>本机密钥安全存储（Windows DPAPI），不明文落盘、不写日志。</summary>
public interface ISecretStore
{
    Task SaveAsync(string secret, CancellationToken cancellationToken);

    Task<string?> TryReadAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>AI 连接测试结果。</summary>
public sealed record AiTestResult(bool Success, string Message);

/// <summary>
/// AI 增强服务：缓存优先、后台生成、失败静默降级。
/// 卡片展示只读取缓存；没有缓存时排队生成，下一次展示即可使用。
/// </summary>
public interface IAiEnrichmentService
{
    Task<WordEnrichment?> GetCachedAsync(int wordId, CancellationToken cancellationToken);

    /// <summary>后台排队生成（不阻塞卡片展示），相同单词只排队一次。</summary>
    void QueueEnrichment(WordCard card);

    /// <summary>立即生成（测试连接或手动触发）。</summary>
    Task<WordEnrichment?> EnrichNowAsync(WordCard card, CancellationToken cancellationToken);

    Task<AiTestResult> TestAsync(string? apiKeyOverride, CancellationToken cancellationToken);

    /// <summary>测试指定连接（设置页当前编辑项，可不保存）。</summary>
    Task<AiTestResult> TestAsync(
        string? apiKeyOverride,
        AiConnection connection,
        CancellationToken cancellationToken);
}

/// <summary>解析模型返回的 JSON（去除 Markdown 代码块，容错字段大小写）。</summary>
public static class AiResponseParser
{
    public static WordEnrichment? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            text = firstLineEnd >= 0 ? text[(firstLineEnd + 1)..] : text;
            var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceIndex >= 0)
            {
                text = text[..fenceIndex];
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;

            var exampleEn = GetString(root, "exampleEn", "example_en");
            var exampleZh = GetString(root, "exampleZh", "example_zh");
            var explanationZh = GetString(root, "explanationZh", "explanation_zh", "explanation");
            var confusableNote = GetString(root, "confusableNote", "confusable_note", "confusable");

            if (exampleEn is null && exampleZh is null && explanationZh is null && confusableNote is null)
            {
                return null;
            }

            return new VocabularyCoach.Core.Entities.WordEnrichment(
                exampleEn,
                exampleZh,
                explanationZh,
                confusableNote);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? GetString(System.Text.Json.JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var value = property.Value.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(value) || value == "null" ? null : value;
            }
        }

        return null;
    }
}
