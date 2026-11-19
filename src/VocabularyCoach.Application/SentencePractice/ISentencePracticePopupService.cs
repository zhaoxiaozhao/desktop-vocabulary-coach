namespace VocabularyCoach.Application.SentencePractice;

/// <summary>由随机弹卡调度器调用，打开一次句子练习弹窗。</summary>
public interface ISentencePracticePopupService
{
    Task<bool> TryShowAsync(
        int sentenceCount,
        CancellationToken cancellationToken = default);
}
