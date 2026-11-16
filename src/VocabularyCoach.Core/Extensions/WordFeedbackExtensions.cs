using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Core.Extensions;

/// <summary>用户反馈到复习算法评分的映射。</summary>
public static class WordFeedbackExtensions
{
    /// <summary>认识 → Good，模糊 → Hard，不认识 → Again。</summary>
    public static ReviewRating ToReviewRating(this WordFeedback feedback) => feedback switch
    {
        WordFeedback.Recognized => ReviewRating.Good,
        WordFeedback.Vague => ReviewRating.Hard,
        WordFeedback.Unknown => ReviewRating.Again,
        _ => throw new ArgumentOutOfRangeException(nameof(feedback), feedback, "未知的反馈类型。"),
    };
}
