using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Core.Extensions;

/// <summary>
/// 卡片结果到掌握反馈的映射。只有打击式反馈（认识/模糊/不认识）才允许进入
/// 复习调度；关闭、稍后提醒、超时一律返回 null，避免误记为已掌握。
/// </summary>
public static class WordCardResultExtensions
{
    /// <summary>将卡片结果转换为掌握反馈，非掌握类结果返回 null。</summary>
    public static WordFeedback? ToFeedbackOrNull(this WordCardResult result) => result switch
    {
        WordCardResult.Recognized => WordFeedback.Recognized,
        WordCardResult.Vague => WordFeedback.Vague,
        WordCardResult.Unknown => WordFeedback.Unknown,
        _ => null,
    };

    /// <summary>判断该结果是否属于有效的掌握反馈。</summary>
    public static bool IsMasteryFeedback(this WordCardResult result) =>
        result.ToFeedbackOrNull() is not null;
}
