using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VocabularyCoach.App.Services;
using VocabularyCoach.App.ViewModels;
using VocabularyCoach.App.Views;
using VocabularyCoach.Application;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.SentencePractice;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Updates;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure;
using VocabularyCoach.Infrastructure.Dictionary;
using VocabularyCoach.Infrastructure.Diagnostics;
using VocabularyCoach.Infrastructure.Logging;

namespace VocabularyCoach.App;

/// <summary>
/// 应用入口：构建 Generic Host、启动后台服务、创建主窗口与托盘图标，
/// 并保证退出时按顺序停止后台任务、释放资源。
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly SingleInstanceGuard _singleInstance = new();

    private IHost? _host;
    private ILogger<App>? _logger;
    private TrayIconService? _trayIcon;
    private MainWindow? _mainWindow;
    private GlobalHotkeyService? _hotkey;
    private CaptureCoordinator? _captureCoordinator;
    private SelectionTranslationCoordinator? _translationCoordinator;
    private ISettingsService? _settingsService;
    private bool _hotkeyWarningShown;
    private bool _translateHotkeyWarningShown;

    /// <summary>是否正在真正退出（用于区分“关闭窗口到托盘”）。</summary>
    public bool IsShuttingDown { get; private set; }

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startHidden = e.Args.Any(
            arg => arg.Equals("--startup", StringComparison.OrdinalIgnoreCase));

        if (!_singleInstance.IsPrimaryInstance)
        {
            if (!startHidden)
            {
                System.Windows.MessageBox.Show(
                    "Desktop Vocabulary Coach 已在运行。\n请在系统托盘（任务栏右下角）中双击图标打开主窗口。",
                    "Desktop Vocabulary Coach",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        try
        {
            _host = BuildHost(e.Args);
            await _host.StartAsync();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("应用启动完成。数据目录：{DataDirectory}", AppPaths.RootDirectory);

            var databaseInitializer = _host.Services.GetRequiredService<DatabaseInitializer>();
            var initialization = await databaseInitializer.InitializeAsync();
            _logger.LogInformation(
                "本地词库就绪：{WordCount} 个单词，种子导入 {SeedImport}。",
                initialization.WordCount,
                initialization.SeedImport?.ToString() ?? "未执行");

            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            _settingsService = settingsService;
            await settingsService.LoadAsync(CancellationToken.None);
            _host.Services.GetRequiredService<ThemeService>().Apply(settingsService.Current.Theme);

            var licenseService = _host.Services.GetRequiredService<ILicenseService>();
            await licenseService.InitializeAsync(CancellationToken.None);
            _logger.LogInformation(
                "用户设置已加载：提醒 {Enabled}，主题 {Theme}，推荐模式 {Mode}。",
                settingsService.Current.RemindersEnabled,
                settingsService.Current.Theme,
                settingsService.Current.RecommendationMode);

            try
            {
                _host.Services.GetRequiredService<ISystemStartupManager>()
                    .Reconcile(settingsService.Current.StartWithWindows);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "同步开机启动设置失败。");
            }

            if (!settingsService.IsInitialized)
            {
                var firstRun = _host.Services.GetRequiredService<FirstRunWindow>();
                firstRun.ShowDialog();

                if (!settingsService.IsInitialized)
                {
                    await settingsService.MarkInitializedAsync(CancellationToken.None);
                }

                if (firstRun.ShowTestCard)
                {
                    try
                    {
                        await _host.Services.GetRequiredService<WordCardService>()
                            .ShowNextCardAsync(LearningSource.Test);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "首次测试卡片展示失败。");
                    }
                }
            }

            var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
            mainViewModel.Initialize(initialization.WordCount);

            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = _mainWindow;
            _mainWindow.HiddenToTray += OnMainWindowHiddenToTray;

            if (!startHidden)
            {
                _mainWindow.Show();
            }

            _trayIcon = _host.Services.GetRequiredService<TrayIconService>();
            _trayIcon.ShowMainWindowRequested += OnShowMainWindowRequested;
            _trayIcon.ExitRequested += OnExitRequested;

            _captureCoordinator = _host.Services.GetRequiredService<CaptureCoordinator>();
            _trayIcon.CaptureRequested += OnCaptureRequested;

            _translationCoordinator = _host.Services.GetRequiredService<SelectionTranslationCoordinator>();
            _trayIcon.TranslateSelectionRequested += OnTranslateSelectionRequested;

            _hotkey = _host.Services.GetRequiredService<GlobalHotkeyService>();
            _hotkey.Pressed += OnHotkeyPressed;
            _hotkey.TranslatePressed += OnTranslateHotkeyPressed;
            settingsService.Changed += OnSettingsChanged;
            UpdateHotkeyRegistration();

            _ = RunUpdateChecksAsync();
        }
        catch (Exception ex)
        {
            WriteFallbackLog("应用启动失败", ex);
            _logger?.LogCritical(ex, "应用启动失败。");

            System.Windows.MessageBox.Show(
                $"应用启动失败：{ex.Message}\n\n详细信息已写入日志目录：\n{AppPaths.LogDirectory}",
                "Desktop Vocabulary Coach",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            RequestExit();
        }
    }

    private IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(
            new FileLoggerProvider(AppPaths.EnsureDirectory(AppPaths.LogDirectory)));

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.Configure<CardDisplayOptions>(
            builder.Configuration.GetSection(CardDisplayOptions.SectionName));

        builder.Services.AddOptions<SchedulerOptions>()
            .Bind(builder.Configuration.GetSection(SchedulerOptions.SectionName))
            .Validate(
                o => o.MinIntervalMinutes > 0 && o.MaxIntervalMinutes >= o.MinIntervalMinutes,
                "Scheduler 的随机间隔配置无效：应满足 0 < MinIntervalMinutes <= MaxIntervalMinutes。")
            .Validate(o => o.MaxPopupsPerHour > 0, "Scheduler:MaxPopupsPerHour 必须大于 0。")
            .Validate(o => o.RecheckDelayMinutes > 0, "Scheduler:RecheckDelayMinutes 必须大于 0。")
            .Validate(o => o.IdleThresholdMinutes >= 0, "Scheduler:IdleThresholdMinutes 不能为负数。")
            .Validate(o => o.HasValidQuietHours, "Scheduler 勿扰时段格式应为 HH:mm。")
            .ValidateOnStart();

        builder.Services.AddOptions<RecommendationOptions>()
            .Bind(builder.Configuration.GetSection(RecommendationOptions.SectionName))
            .Validate(o => o.NewWordRatio is >= 0 and <= 1, "Recommendation:NewWordRatio 必须在 0–1 之间。")
            .Validate(o => o.ReviewPressureThreshold >= 0, "Recommendation:ReviewPressureThreshold 不能为负数。")
            .Validate(o => o.OverdueGraceHours >= 0, "Recommendation:OverdueGraceHours 不能为负数。")
            .Validate(o => o.ReviewCandidateLimit > 0, "Recommendation:ReviewCandidateLimit 必须大于 0。")
            .ValidateOnStart();

        builder.Services.AddSingleton(Dispatcher.CurrentDispatcher);
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<WindowLauncher>();
        builder.Services.AddSingleton<ISelectedTextReader, WindowsSelectedTextReader>();
        builder.Services.AddSingleton<GlobalHotkeyService>();
        builder.Services.AddSingleton<SelectionReplacer>();
        builder.Services.AddSingleton<SelectionTranslationCoordinator>();
        builder.Services.AddSingleton<ISentencePracticePopupService, WpfSentencePracticePopupService>();
        builder.Services.AddSingleton<CaptureCoordinator>();
        builder.Services.AddSingleton<WpfWordPopupPresenter>();
        builder.Services.AddSingleton<IWordPopupPresenter>(
            sp => sp.GetRequiredService<WpfWordPopupPresenter>());
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsWindow>();
        builder.Services.AddTransient<StatisticsViewModel>();
        builder.Services.AddTransient<StatisticsWindow>();
        builder.Services.AddTransient<FirstRunViewModel>();
        builder.Services.AddTransient<FirstRunWindow>();
        builder.Services.AddTransient<CapturedWordsViewModel>();
        builder.Services.AddTransient<CapturedWordsWindow>();
        builder.Services.AddTransient<LicenseViewModel>();
        builder.Services.AddTransient<LicenseWindow>();
        builder.Services.AddTransient<UpdateViewModel>();
        builder.Services.AddTransient<UpdateWindow>();
        builder.Services.AddTransient<SentencePracticeViewModel>();
        builder.Services.AddTransient<SentencePracticeWindow>();

        return builder.Build();
    }

    private void OnShowMainWindowRequested(object? sender, EventArgs e) => ShowMainWindow();

    private void OnCaptureRequested(object? sender, EventArgs e) => _captureCoordinator?.CaptureManually();

    private void OnTranslateSelectionRequested(object? sender, EventArgs e) =>
        _ = _translationCoordinator?.TranslateSelectionAsync();

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_captureCoordinator is not null)
        {
            _ = _captureCoordinator.CaptureFromSelectionAsync();
        }
    }

    private void OnTranslateHotkeyPressed(object? sender, EventArgs e)
    {
        if (_translationCoordinator is not null)
        {
            _ = _translationCoordinator.TranslateSelectionAsync();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => UpdateHotkeyRegistration();

    /// <summary>启动后延迟检查更新，之后按配置间隔重复；发现新版本只提示，不强制安装。</summary>
    private async Task RunUpdateChecksAsync()
    {
        if (_host is null)
        {
            return;
        }

        var options = _host.Services.GetRequiredService<IOptions<UpdateOptions>>().Value;
        var updateService = _host.Services.GetRequiredService<IUpdateService>();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.CheckDelaySeconds)));
        }
        catch
        {
            return;
        }

        while (!IsShuttingDown)
        {
            try
            {
                var result = await updateService.CheckAsync();
                if (result.UpdateAvailable && result.Manifest is { } manifest)
                {
                    Dispatcher.Invoke(() => _trayIcon?.ShowInfo(
                        "发现新版本",
                        $"{manifest.Version} 已发布，可在托盘菜单「检查更新…」中查看。"));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "后台检查更新失败。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(1, options.CheckIntervalHours)));
            }
            catch
            {
                return;
            }
        }
    }

    private void UpdateHotkeyRegistration()
    {
        if (_hotkey is null || _settingsService is null)
        {
            return;
        }

        if (!_settingsService.Current.CaptureEnabled)
        {
            _hotkey.Unregister();
        }
        else
        {
            var registered = _hotkey.TryRegister(_settingsService.Current.CaptureHotkey);
            if (registered)
            {
                _hotkeyWarningShown = false;
            }
            else if (!_hotkeyWarningShown)
            {
                _hotkeyWarningShown = true;
                _logger?.LogWarning("全局热键注册失败。");
                _trayIcon?.ShowInfo(
                    "快捷键不可用",
                    $"{_settingsService.Current.CaptureHotkey} 可能已被其他程序占用，可在设置中更换热键。");
            }
        }

        if (!_settingsService.Current.ChineseToEnglishEnabled)
        {
            _hotkey.UnregisterTranslate();
        }
        else
        {
            var registered = _hotkey.TryRegisterTranslate(_settingsService.Current.TranslateHotkey);
            if (registered)
            {
                _translateHotkeyWarningShown = false;
            }
            else if (!_translateHotkeyWarningShown)
            {
                _translateHotkeyWarningShown = true;
                _logger?.LogWarning("中文转英文热键注册失败。");
                _trayIcon?.ShowInfo(
                    "中文转英文快捷键不可用",
                    $"{_settingsService.Current.TranslateHotkey} 可能已被其他程序占用，可在设置中更换热键。");
            }
        }
    }

    private void OnExitRequested(object? sender, EventArgs e) => RequestExit();

    private void OnMainWindowHiddenToTray(object? sender, EventArgs e)
    {
        _logger?.LogInformation("主窗口已隐藏到系统托盘。");
        _trayIcon?.NotifyStillRunning();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        // Topmost 瞬时切换可确保窗口从其他窗口之后移到前台。
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    /// <summary>请求彻底退出：停掉后台服务并释放数据库连接。</summary>
    public void RequestExit()
    {
        if (IsShuttingDown)
        {
            return;
        }

        IsShuttingDown = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("应用正在退出。");

        try
        {
            _settingsService?.Changed -= OnSettingsChanged;
            _hotkey?.Dispose();
            _hotkey = null;

            _trayIcon?.Dispose();
            _trayIcon = null;

            if (_host is not null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    _host.StopAsync(timeout.Token).GetAwaiter().GetResult();
                    _logger?.LogInformation("后台服务已停止。");
                }
                finally
                {
                    _host.Dispose();
                    _host = null;
                }
            }
        }
        catch (Exception ex)
        {
            WriteFallbackLog("退出清理失败", ex);
        }
        finally
        {
            _singleInstance.Dispose();
            base.OnExit(e);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "UI 线程未处理异常。");
        WriteFallbackLog("UI 线程未处理异常", e.Exception);

        // 兜底：记录后继续运行，避免托盘应用因单次 UI 异常直接崩溃。
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "应用域未处理异常。");
            WriteFallbackLog("应用域未处理异常", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "未观察的后台任务异常。");
        e.SetObserved();
    }

    private static void WriteFallbackLog(string title, Exception exception)
    {
        try
        {
            var directory = AppPaths.EnsureDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(directory, "crash.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 最后兜底，不能再抛异常。
        }
    }
}
