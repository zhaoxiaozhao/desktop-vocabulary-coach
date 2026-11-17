using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.SentencePractice;

/// <summary>离线句库中的一条句子练习。</summary>
public sealed record SentenceSeedEntry(
    string Word,
    string Sentence,
    string Answer,
    string TranslationZh,
    string? Pattern,
    string? GrammarNote,
    IReadOnlyList<string> Distractors,
    IReadOnlyList<string>? AlternativeAnswers = null);

/// <summary>一次句子填空练习（离线或 AI 生成）。</summary>
public sealed record SentencePracticeItem(
    int WordId,
    string Word,
    string Sentence,
    string Answer,
    IReadOnlyList<string> AlternativeAnswers,
    string TranslationZh,
    string? Pattern,
    string? GrammarNote,
    IReadOnlyList<string> Options,
    bool FromAi);

/// <summary>提交结果。</summary>
public sealed record SentencePracticeOutcome(bool IsCorrect, string ExpectedAnswer);

/// <summary>一次句子练习会话；包含 AI 是否成功，便于界面提示回退。</summary>
public sealed record SentencePracticeSession(
    IReadOnlyList<SentencePracticeItem> Items,
    bool AiEnabled,
    bool AiUsed,
    string? AiMessage);

/// <summary>离线句库。</summary>
public interface ISentenceBank
{
    IReadOnlyList<SentenceSeedEntry> Entries { get; }
}

/// <summary>句子练习会话与结果落库。</summary>
public interface ISentencePracticeService
{
    Task<SentencePracticeSession> CreateSessionAsync(
        int count,
        CancellationToken cancellationToken = default);

    Task<SentencePracticeOutcome> SubmitAsync(
        SentencePracticeItem item,
        string answer,
        DateTimeOffset shownAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 可选的 AI 句子生成器。AI 关闭、未配置或生成失败时返回空列表，
/// 由离线句库兜底。
/// </summary>
public interface ISentencePracticeGenerator
{
    Task<IReadOnlyList<SentencePracticeItem>> GenerateAsync(
        IReadOnlyList<WordCard> cards,
        CancellationToken cancellationToken = default);
}
