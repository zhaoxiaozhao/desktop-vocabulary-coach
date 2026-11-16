namespace VocabularyCoach.Core.Enums;

/// <summary>单词在用户侧的掌握阶段。</summary>
public enum WordProgressStatus
{
    /// <summary>尚未学习。</summary>
    New = 1,

    /// <summary>学习中。</summary>
    Learning = 2,

    /// <summary>复习阶段。</summary>
    Review = 3,

    /// <summary>已掌握。</summary>
    Mastered = 4,
}
