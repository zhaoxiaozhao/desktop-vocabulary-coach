using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Ai;

/// <summary>离线 Mock 增强，用于演示与自动化测试，不访问网络。</summary>
public sealed class MockVocabularyEnrichmentProvider : IVocabularyEnrichmentProvider
{
    public string Kind => "Mock";

    public Task<WordEnrichment?> EnrichAsync(AiEnrichmentRequest request, CancellationToken cancellationToken)
    {
        var definition = string.IsNullOrWhiteSpace(request.DefinitionZh)
            ? "（本地词库释义）"
            : request.DefinitionZh;

        var enrichment = new WordEnrichment(
            ExampleEn: $"This is a mock example sentence using \"{request.Word}\".",
            ExampleZh: $"这是使用 “{request.Word}” 的示例句子。",
            ExplanationZh: $"Mock 简释：{request.Word} — {definition}",
            ConfusableNote: null);

        return Task.FromResult<WordEnrichment?>(enrichment);
    }
}
