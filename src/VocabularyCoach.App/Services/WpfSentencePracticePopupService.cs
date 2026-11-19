using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.SentencePractice;

namespace VocabularyCoach.App.Services;

/// <summary>把调度器的句子练习请求切换到 WPF UI 线程。</summary>
public sealed class WpfSentencePracticePopupService : ISentencePracticePopupService
{
    private readonly Dispatcher _dispatcher;
    private readonly WindowLauncher _windowLauncher;
    private readonly ILogger<WpfSentencePracticePopupService> _logger;

    public WpfSentencePracticePopupService(
        Dispatcher dispatcher,
        WindowLauncher windowLauncher,
        ILogger<WpfSentencePracticePopupService> logger)
    {
        _dispatcher = dispatcher;
        _windowLauncher = windowLauncher;
        _logger = logger;
    }

    public async Task<bool> TryShowAsync(
        int sentenceCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dispatcher
                .InvokeAsync(
                    () => _windowLauncher.ShowSentencePractice(sentenceCount),
                    DispatcherPriority.Normal,
                    cancellationToken)
                .Task
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开随机句子练习失败。");
            return false;
        }
    }
}
