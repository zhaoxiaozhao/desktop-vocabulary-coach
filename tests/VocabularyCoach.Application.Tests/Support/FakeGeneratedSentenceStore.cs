using VocabularyCoach.Application.SentencePractice;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class FakeGeneratedSentenceStore : IGeneratedSentenceStore
{
    private readonly List<GeneratedSentence> _sentences;

    public FakeGeneratedSentenceStore(IEnumerable<GeneratedSentence>? sentences = null)
    {
        _sentences = sentences?.ToList() ?? [];
    }

    public Task<IReadOnlyList<GeneratedSentence>> GetRandomAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GeneratedSentence>>(
            _sentences.Take(count).ToList());

    public Task<GeneratedSentence?> GetRandomForWordAsync(
        int wordId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sentences.FirstOrDefault(x => x.WordId == wordId));

    public Task<int> CountSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sentences.Count(x => x.CreatedAt >= sinceUtc));

    public Task<int> CountForWordAsync(
        int wordId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sentences.Count(x => x.WordId == wordId));

    public Task AddAsync(
        IEnumerable<GeneratedSentence> sentences,
        CancellationToken cancellationToken = default)
    {
        _sentences.AddRange(sentences);
        return Task.CompletedTask;
    }
}
