using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>ReviewLogs 表：每次复习评分的历史记录。</summary>
public sealed class ReviewLogRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public WordRecord? Word { get; set; }

    public ReviewRating Rating { get; set; }

    public WordFeedback UserFeedback { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    public DateTimeOffset? PreviousDueAt { get; set; }

    public DateTimeOffset? NextDueAt { get; set; }

    public LearningSource Source { get; set; }
}
