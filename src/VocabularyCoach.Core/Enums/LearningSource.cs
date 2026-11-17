namespace VocabularyCoach.Core.Enums;

/// <summary>本次学习记录的来源。</summary>
public enum LearningSource
{
    /// <summary>后台调度自动弹出。</summary>
    Popup = 1,

    /// <summary>用户从主窗口或托盘手动弹出。</summary>
    Manual = 2,

    /// <summary>测试弹窗。</summary>
    Test = 3,

    /// <summary>句子填空练习。</summary>
    SentencePractice = 4,
}
