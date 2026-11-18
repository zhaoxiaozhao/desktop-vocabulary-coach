using VocabularyCoach.Application.Ai;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class FakeAiEnrichmentService : IAiEnrichmentService
{
    public WordEnrichment? Cached { get; set; }

    public List<WordCard> Queued { get; } = [];

    public int EnrichNowCalls { get; private set; }

    public Task<WordEnrichment?> GetCachedAsync(int wordId, CancellationToken cancellationToken) =>
        Task.FromResult(Cached);

    public void QueueEnrichment(WordCard card) => Queued.Add(card);

    public Task<WordEnrichment?> EnrichNowAsync(WordCard card, CancellationToken cancellationToken)
    {
        EnrichNowCalls++;
        return Task.FromResult(Cached);
    }

    public Task<AiTestResult> TestAsync(string? apiKeyOverride, CancellationToken cancellationToken) =>
        Task.FromResult(new AiTestResult(true, "ok"));

    public Task<AiTestResult> TestAsync(
        string? apiKeyOverride,
        VocabularyCoach.Application.Settings.AiConnection connection,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AiTestResult(true, "ok"));
}

internal sealed class FakeSecretStore : ISecretStore
{
    public string? Secret { get; set; }

    public Task SaveAsync(string secret, CancellationToken cancellationToken)
    {
        Secret = secret;
        return Task.CompletedTask;
    }

    public Task<string?> TryReadAsync(CancellationToken cancellationToken) => Task.FromResult(Secret);

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Secret = null;
        return Task.CompletedTask;
    }
}
