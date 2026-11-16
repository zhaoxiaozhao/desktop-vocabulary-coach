using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;

namespace VocabularyCoach.Infrastructure.Audio;

/// <summary>
/// 基于 Windows SAPI 的系统 TTS 发音。发音失败只记录警告并静默降级，
/// 不阻塞卡片交互。
/// </summary>
public sealed class SystemSpeechPronouncer : IAudioPronouncer, IDisposable
{
    private readonly ILogger<SystemSpeechPronouncer> _logger;
    private readonly Lazy<SpeechSynthesizer?> _synthesizer;
    private readonly SemaphoreSlim _speakGate = new(1, 1);

    public SystemSpeechPronouncer(ILogger<SystemSpeechPronouncer> logger)
    {
        _logger = logger;
        _synthesizer = new Lazy<SpeechSynthesizer?>(CreateSynthesizer);
    }

    public bool IsAvailable => _synthesizer.Value is not null;

    public async Task SpeakAsync(string word, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        var synthesizer = _synthesizer.Value;
        if (synthesizer is null)
        {
            return;
        }

        await _speakGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            synthesizer.SpeakAsyncCancelAll();
            await Task.Run(() => synthesizer.Speak(word), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            synthesizer.SpeakAsyncCancelAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "朗读单词失败：{Word}", word);
        }
        finally
        {
            _speakGate.Release();
        }
    }

    private SpeechSynthesizer? CreateSynthesizer()
    {
        try
        {
            var synthesizer = new SpeechSynthesizer();
            synthesizer.SetOutputToDefaultAudioDevice();
            synthesizer.Rate = 0;
            return synthesizer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "系统语音合成不可用，发音按钮将被禁用。");
            return null;
        }
    }

    public void Dispose()
    {
        if (_synthesizer.IsValueCreated)
        {
            _synthesizer.Value?.Dispose();
        }

        _speakGate.Dispose();
    }
}
