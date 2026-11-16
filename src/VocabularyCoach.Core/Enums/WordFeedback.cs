namespace VocabularyCoach.Core.Enums;

/// <summary>
/// 用户对单词的掌握反馈。只有这三种反馈会更新复习状态。
/// </summary>
public enum WordFeedback
{
    /// <summary>认识：当前回忆成功，拉长复习间隔。</summary>
    Recognized = 1,

    /// <summary>模糊：有印象但不牢固，短间隔复习。</summary>
    Vague = 2,

    /// <summary>不认识：当前未能回忆，尽快再次学习。</summary>
    Unknown = 3,
}
