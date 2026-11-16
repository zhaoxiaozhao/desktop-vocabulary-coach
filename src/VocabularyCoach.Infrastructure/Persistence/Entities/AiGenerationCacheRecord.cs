namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>AiGenerationCache 表：缓存 AI 生成的释义/例句，避免重复请求。Sprint 6 使用。</summary>
public sealed class AiGenerationCacheRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public WordRecord? Word { get; set; }

    /// <summary>Provider 标识，例如 openai-compatible。</summary>
    public string Provider { get; set; } = string.Empty;

    public string? Model { get; set; }

    /// <summary>生成内容（JSON 或纯文本），由调用方解释。</summary>
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
