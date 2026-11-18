using VocabularyCoach.Application.Ai;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class FakeAiCacheStore : IAiCacheStore
{
    public WordEnrichment? Cached { get; set; }

    public int SaveCalls { get; private set; }

    public int GetCalls { get; private set; }

    public Task<WordEnrichment?> GetAsync(int wordId, string provider, CancellationToken cancellationToken)
    {
        GetCalls++;
        return Task.FromResult(Cached);
    }

    public Task SaveAsync(
        int wordId,
        string provider,
        string model,
        WordEnrichment enrichment,
        CancellationToken cancellationToken)
    {
        SaveCalls++;
        Cached = enrichment;
        return Task.CompletedTask;
    }
}

internal sealed class FakeEnrichmentProvider : IVocabularyEnrichmentProvider
{
    private readonly string _kind;

    public FakeEnrichmentProvider(string kind) => _kind = kind;

    public string Kind => _kind;

    public int CallCount { get; private set; }

    public WordEnrichment? Result { get; set; }

    public Exception? Exception { get; set; }

    public Func<AiEnrichmentRequest, WordEnrichment?>? Handler { get; set; }

    public Task<WordEnrichment?> EnrichAsync(AiEnrichmentRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        if (Exception is not null)
        {
            return Task.FromException<WordEnrichment?>(Exception);
        }

        return Task.FromResult(Handler?.Invoke(request) ?? Result);
    }
}
