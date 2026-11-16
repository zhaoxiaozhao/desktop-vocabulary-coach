namespace VocabularyCoach.Application.Abstractions;

/// <summary>
/// 单词发音能力。实现可以是系统 TTS，也可以是后续的可替换音频资源。
/// </summary>
public interface IAudioPronouncer
{
    /// <summary>当前环境是否可用；不可用时 UI 应隐藏发音按钮。</summary>
    bool IsAvailable { get; }

    /// <summary>朗读单词，失败时静默降级（发音不是核心功能）。</summary>
    Task SpeakAsync(string word, CancellationToken cancellationToken);
}
