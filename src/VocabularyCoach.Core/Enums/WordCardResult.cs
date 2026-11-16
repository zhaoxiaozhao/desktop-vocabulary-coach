namespace VocabularyCoach.Core.Enums;

/// <summary>
/// 一次卡片展示的最终结果。关闭、稍后提醒和超时都不代表掌握状态发生变化。
/// </summary>
public enum WordCardResult
{
    /// <summary>用户点击“认识”。</summary>
    Recognized = 1,

    /// <summary>用户点击“模糊”。</summary>
    Vague = 2,

    /// <summary>用户点击“不认识”。</summary>
    Unknown = 3,

    /// <summary>用户主动关闭卡片，未提供反馈。</summary>
    Dismissed = 4,

    /// <summary>用户选择稍后提醒。</summary>
    Snoozed = 5,

    /// <summary>卡片超时自动关闭，未提供反馈。</summary>
    Timeout = 6,
}
