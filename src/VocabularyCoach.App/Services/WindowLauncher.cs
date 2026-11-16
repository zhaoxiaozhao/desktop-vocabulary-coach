using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VocabularyCoach.App.Views;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 打开设置/统计窗口。窗口是惰性创建的，关闭后释放，避免持有过期数据。
/// </summary>
public sealed class WindowLauncher
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WindowLauncher> _logger;

    private SettingsWindow? _settingsWindow;
    private StatisticsWindow? _statisticsWindow;
    private CapturedWordsWindow? _capturedWordsWindow;

    public WindowLauncher(IServiceProvider services, ILogger<WindowLauncher> logger)
    {
        _services = services;
        _logger = logger;
    }

    public void ShowSettings()
    {
        try
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = _services.GetRequiredService<SettingsWindow>();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            ShowWindow(_settingsWindow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开设置窗口失败。");
        }
    }

    public void ShowStatistics()
    {
        try
        {
            if (_statisticsWindow is null)
            {
                _statisticsWindow = _services.GetRequiredService<StatisticsWindow>();
                _statisticsWindow.Closed += (_, _) => _statisticsWindow = null;
            }

            ShowWindow(_statisticsWindow);
            _statisticsWindow.Refresh();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开统计窗口失败。");
        }
    }

    public void ShowCapturedWords()
    {
        try
        {
            if (_capturedWordsWindow is null)
            {
                _capturedWordsWindow = _services.GetRequiredService<CapturedWordsWindow>();
                _capturedWordsWindow.Closed += (_, _) => _capturedWordsWindow = null;
            }

            ShowWindow(_capturedWordsWindow);
            _capturedWordsWindow.Refresh();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开生词本失败。");
        }
    }

    private static void ShowWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
