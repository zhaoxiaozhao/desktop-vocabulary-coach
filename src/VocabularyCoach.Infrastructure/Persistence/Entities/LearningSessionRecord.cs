using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>LearningSessions 表：每次卡片展示与用户行为，用于统计和自适应。</summary>
public sealed class LearningSessionRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public WordRecord? Word { get; set; }

    public DateTimeOffset ShownAt { get; set; }

    public DateTimeOffset? RespondedAt { get; set; }

    public WordCardResult Action { get; set; }

    /// <summary>卡片展示到结束的时长（毫秒）。</summary>
    public int? DurationMs { get; set; }

    public LearningSource Source { get; set; }
}
