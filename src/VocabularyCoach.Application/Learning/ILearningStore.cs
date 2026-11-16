using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Learning;

/// <summary>到期待复习的词及其到期时间。</summary>
public sealed record DueReview(WordCard Card, DateTimeOffset DueAt);

/// <summary>一次卡片展示的完整结果，用于事务性写入学习会话与复习记录。</summary>
public sealed record CardOutcome(
    int WordId,
    LearningSource Source,
    DateTimeOffset ShownAt,
    DateTimeOffset RespondedAt,
    int DurationMs,
    WordCardResult Result,
    WordFeedback? Feedback,
    ReviewScheduleResult? Schedule,
    DateTimeOffset? PreviousDueAt);

/// <summary>
/// 学习进度与复习记录的持久化抽象。Application 只负责业务决策，
/// 具体 SQLite 写入由 Infrastructure 实现，并保证单次反馈的原子性。
/// </summary>
public interface ILearningStore
{
    /// <summary>读取单词的复习状态；从未学习过返回 null。</summary>
    Task<ReviewState?> GetReviewStateAsync(int wordId, CancellationToken cancellationToken);

    /// <summary>原子写入进度更新、复习日志与学习会话。非掌握反馈只写会话。</summary>
    Task SaveCardOutcomeAsync(CardOutcome outcome, CancellationToken cancellationToken);

    /// <summary>按到期时间升序取到期复习词。</summary>
    Task<IReadOnlyList<DueReview>> GetDueReviewsAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>当前目标新词难度（来自用户学习档案）。</summary>
    Task<int> GetCurrentDifficultyAsync(CancellationToken cancellationToken);

    /// <summary>到期复习词数量（供统计与推荐判断）。</summary>
    Task<int> CountDueReviewsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>读取学习档案（难度、倾向、最近反馈统计）。</summary>
    Task<LearningProfileSnapshot> GetProfileAsync(CancellationToken cancellationToken);

    /// <summary>写入学习档案。</summary>
    Task SaveProfileAsync(LearningProfileSnapshot profile, CancellationToken cancellationToken);

    /// <summary>按时间倒序读取最近 N 次有效掌握反馈（认识/模糊/不认识）。</summary>
    Task<IReadOnlyList<WordFeedback>> GetRecentMasteryFeedbackAsync(
        int limit,
        CancellationToken cancellationToken);
}
