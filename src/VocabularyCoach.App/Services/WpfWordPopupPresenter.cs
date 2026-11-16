using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VocabularyCoach.App.ViewModels;
using VocabularyCoach.App.Views;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.App.Services;

/// <summary>
/// WPF 卡片展示器：把后台服务的展示请求切换到 UI 线程，
/// 同一时间只允许一个卡片窗口。
/// </summary>
public sealed class WpfWordPopupPresenter : IWordPopupPresenter
{
    private readonly Dispatcher _dispatcher;
    private readonly IAudioPronouncer _pronouncer;
    private readonly ISettingsService _settings;
    private readonly ILogger<WpfWordPopupPresenter> _logger;

    private WordCardWindow? _currentWindow;

    public WpfWordPopupPresenter(
        Dispatcher dispatcher,
        IAudioPronouncer pronouncer,
        ISettingsService settings,
        ILogger<WpfWordPopupPresenter> logger)
    {
        _dispatcher = dispatcher;
        _pronouncer = pronouncer;
        _settings = settings;
        _logger = logger;
    }

    public Task<WordCardResult> ShowAsync(WordCard card, CancellationToken cancellationToken)
    {
        return _dispatcher
            .InvokeAsync(() => ShowCoreAsync(card, cancellationToken))
            .Task
            .Unwrap();
    }

    private async Task<WordCardResult> ShowCoreAsync(WordCard card, CancellationToken cancellationToken)
    {
        if (_currentWindow is not null)
        {
            throw new InvalidOperationException("已有单词卡片正在显示。");
        }

        var viewModel = new WordCardViewModel(card, _pronouncer, _settings.Card);
        var window = new WordCardWindow(viewModel, _settings.Card);
        _currentWindow = window;

        try
        {
            window.Show();

            using var registration = cancellationToken.Register(
                () => window.RequestClose(WordCardResult.Dismissed));

            var result = await window.Result.ConfigureAwait(true);
            _logger.LogDebug("卡片 {Word} 已关闭，结果 {Result}。", card.Word, result);
            return result;
        }
        finally
        {
            if (ReferenceEquals(_currentWindow, window))
            {
                _currentWindow = null;
            }
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        return _dispatcher
            .InvokeAsync(() => _currentWindow?.RequestClose(WordCardResult.Dismissed))
            .Task;
    }
}
