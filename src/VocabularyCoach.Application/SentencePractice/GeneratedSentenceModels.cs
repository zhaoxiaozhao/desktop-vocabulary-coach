namespace VocabularyCoach.Application.SentencePractice;

/// <summary>后台 AI 生成并持久化到离线库的例句。</summary>
public sealed record GeneratedSentence(
    int WordId,
    string Word,
    string Sentence,
    string Answer,
    IReadOnlyList<string> AlternativeAnswers,
    string TranslationZh,
    string? Pattern,
    string? GrammarNote,
    IReadOnlyList<string> Options,
    string Model,
    DateTimeOffset CreatedAt)
{
    public string ExampleSentence => Sentence.Replace("____", Answer, StringComparison.Ordinal);
}

/// <summary>本地例句库（后台生成 + 供句子练习/弹窗使用）。</summary>
public interface IGeneratedSentenceStore
{
    Task<IReadOnlyList<GeneratedSentence>> GetRandomAsync(
        int count,
        CancellationToken cancellationToken = default);

    Task<GeneratedSentence?> GetRandomForWordAsync(
        int wordId,
        CancellationToken cancellationToken = default);

    Task<int> CountSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    Task<int> CountForWordAsync(
        int wordId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        IEnumerable<GeneratedSentence> sentences,
        CancellationToken cancellationToken = default);
}
