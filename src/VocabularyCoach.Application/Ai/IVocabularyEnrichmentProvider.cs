using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Ai;

/// <summary>一次 AI 增强请求。Provider 无状态，配置随请求传入，便于切换/测试。</summary>
public sealed record AiEnrichmentRequest
{
    public required string Word { get; init; }

    public string? DefinitionZh { get; init; }

    public int Difficulty { get; init; } = 2;

    public required string BaseUrl { get; init; }

    public required string Model { get; init; }

    public string ApiKey { get; init; } = string.Empty;
}

/// <summary>AI 增强提供者（Mock 或 OpenAI-compatible）。</summary>
public interface IVocabularyEnrichmentProvider
{
    /// <summary>实现类型标识："Mock" / "OpenAICompatible"。</summary>
    string Kind { get; }

    Task<WordEnrichment?> EnrichAsync(AiEnrichmentRequest request, CancellationToken cancellationToken);
}
