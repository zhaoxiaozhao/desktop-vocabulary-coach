namespace VocabularyCoach.Application.Settings;

/// <summary>推荐模式。</summary>
public enum RecommendationMode
{
    /// <summary>新词与到期复习混合推荐。</summary>
    Smart = 1,

    /// <summary>只推新词（忽略到期复习）。</summary>
    NewWordsOnly = 2,

    /// <summary>只推到期复习；没有到期词时不弹。</summary>
    ReviewOnly = 3,
}

/// <summary>难度模式。</summary>
public enum DifficultyMode
{
    /// <summary>根据最近反馈自动调整。</summary>
    Adaptive = 1,

    /// <summary>使用固定难度。</summary>
    Manual = 2,
}

/// <summary>卡片主题。</summary>
public enum CardTheme
{
    Light = 1,
    Dark = 2,
}

/// <summary>卡片交互方式。</summary>
public enum InteractionMode
{
    /// <summary>自我评估：认识 / 模糊 / 不认识。</summary>
    SelfReport = 1,

    /// <summary>选择释义：从 4 个选项中选出正确中文释义。</summary>
    ChoiceQuiz = 2,

    /// <summary>混合：随机一半使用选择释义，其余自我评估。</summary>
    Mixed = 3,
}
