using System.Drawing;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Enums;
using Forms = System.Windows.Forms;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 系统托盘图标与菜单。菜单动作只调用 Application 层服务，
/// 真正的退出由 <see cref="ExitRequested"/> 交给 App 处理。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string IconUri = "pack://application:,,,/VocabularyCoach;component/Resources/app.ico";

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly WordCardService _wordCardService;
    private readonly PauseState _pauseState;
    private readonly WindowLauncher _windowLauncher;
    private readonly ISettingsService _settings;
    private readonly ILogger<TrayIconService> _logger;

    public TrayIconService(
        WordCardService wordCardService,
        PauseState pauseState,
        WindowLauncher windowLauncher,
        ISettingsService settings,
        ILogger<TrayIconService> logger)
    {
        _wordCardService = wordCardService;
        _pauseState = pauseState;
        _windowLauncher = windowLauncher;
        _settings = settings;
        _logger = logger;

        _menu = new Forms.ContextMenuStrip();
        _menu.Opening += (_, _) => RebuildMenu();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Desktop Vocabulary Coach",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);

        _logger.LogInformation("系统托盘图标已就绪。");
    }

    /// <summary>请求显示主窗口。</summary>
    public event EventHandler? ShowMainWindowRequested;

    /// <summary>请求彻底退出应用。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>请求打开生词捕获窗口（手动输入）。</summary>
    public event EventHandler? CaptureRequested;

    /// <summary>关闭主窗口时提示用户应用仍在后台运行。</summary>
    public void NotifyStillRunning()
    {
        ShowBalloon("Desktop Vocabulary Coach", "应用仍在系统托盘中运行，双击托盘图标可重新打开主窗口。");
    }

    /// <summary>显示托盘气泡信息。</summary>
    public void ShowInfo(string title, string message) => ShowBalloon(title, message);

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add(CreateItem("显示主窗口", (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new Forms.ToolStripSeparator());

        var pausedUntil = _pauseState.PausedUntil;
        if (pausedUntil is { } until)
        {
            _menu.Items.Add(CreateItem($"恢复提醒（暂停至 {until:HH:mm}）", (_, _) => ResumeReminders()));
        }

        var pause30 = CreateItem("暂停提醒 30 分钟", (_, _) => PauseReminders(TimeSpan.FromMinutes(30), "30 分钟"));
        var pause60 = CreateItem("暂停提醒 1 小时", (_, _) => PauseReminders(TimeSpan.FromHours(1), "1 小时"));
        var pauseTomorrow = CreateItem("暂停提醒至明天", (_, _) => PauseUntilTomorrow());

        pause30.Enabled = pausedUntil is null;
        pause60.Enabled = pausedUntil is null;
        pauseTomorrow.Enabled = pausedUntil is null;

        _menu.Items.Add(pause30);
        _menu.Items.Add(pause60);
        _menu.Items.Add(pauseTomorrow);
        _menu.Items.Add(new Forms.ToolStripSeparator());

        _menu.Items.Add(CreateItem("立即弹出一个单词", (_, _) => ShowWordNow()));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("记录生词（Ctrl+Alt+W）", (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(CreateItem("生词本", (_, _) => _windowLauncher.ShowCapturedWords()));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("今日学习统计", (_, _) => _windowLauncher.ShowStatistics()));
        _menu.Items.Add(CreateItem("设置", (_, _) => _windowLauncher.ShowSettings()));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(CreateItem("退出", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler onClick)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    private void PauseReminders(TimeSpan duration, string label)
    {
        _pauseState.Pause(duration);
        _logger.LogInformation("用户暂停提醒：{Label}。", label);
        ShowBalloon("提醒已暂停", $"将在 {label} 后自动恢复。");
    }

    private void PauseUntilTomorrow()
    {
        var resumeTime = TimeOnly.TryParse(_settings.Current.PauseUntilTomorrowTime, out var parsed)
            ? parsed
            : TimeOnly.MinValue;

        _pauseState.PauseUntilTomorrow(resumeTime);
        _logger.LogInformation("用户暂停提醒至明天 {Time:HH\\:mm}。", resumeTime);
        ShowBalloon("提醒已暂停", $"将在明天 {resumeTime:HH\\:mm} 恢复提醒。");
    }

    private void ResumeReminders()
    {
        _pauseState.Resume();
        _logger.LogInformation("用户恢复提醒。");
        ShowBalloon("提醒已恢复", "后台调度将按正常间隔弹出单词。");
    }

    private void ShowWordNow()
    {
        _ = ShowWordNowAsync();
    }

    private async Task ShowWordNowAsync()
    {
        try
        {
            var result = await _wordCardService.ShowNextCardAsync(LearningSource.Manual);
            _logger.LogInformation("托盘手动弹出卡片，结果：{Result}", result?.ToString() ?? "无可用单词");
        }
        catch (InvalidOperationException)
        {
            ShowBalloon("请稍候", "已有一张单词卡片正在显示。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "托盘手动弹出卡片失败。");
            ShowBalloon("弹出失败", "详细信息已写入日志。");
        }
    }

    private void ShowBalloon(string title, string message)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "显示托盘气泡失败。");
        }
    }

    private static Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri(IconUri));
        using var stream = resource.Stream;
        return new Icon(stream);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _menu.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }
}
