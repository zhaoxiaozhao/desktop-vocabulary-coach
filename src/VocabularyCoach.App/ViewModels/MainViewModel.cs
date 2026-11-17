using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.App.Services;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure.Diagnostics;

namespace VocabularyCoach.App.ViewModels;

/// <summary>主窗口视图模型：查词、导入、手动弹卡与运行状态。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly WordCardService _wordCardService;
    private readonly PauseState _pauseState;
    private readonly IWordLibrary _wordLibrary;
    private readonly IWordLibraryImporter _importer;
    private readonly ISchedulerStatus _schedulerStatus;
    private readonly ILicenseService _license;
    private readonly WindowLauncher _windowLauncher;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        WordCardService wordCardService,
        PauseState pauseState,
        IWordLibrary wordLibrary,
        IWordLibraryImporter importer,
        ISchedulerStatus schedulerStatus,
        ILicenseService license,
        WindowLauncher windowLauncher,
        ILogger<MainViewModel> logger)
    {
        _wordCardService = wordCardService;
        _pauseState = pauseState;
        _wordLibrary = wordLibrary;
        _importer = importer;
        _schedulerStatus = schedulerStatus;
        _license = license;
        _windowLauncher = windowLauncher;
        _logger = logger;
        _license.Changed += OnLicenseChanged;
        RefreshState();
    }

    public string AppVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";

    public string VersionSummary => $"版本 {AppVersion}";

    public string LicenseSummary
    {
        get
        {
            var snapshot = _license.Current;
            return snapshot.Status switch
            {
                LicenseStatus.Licensed => snapshot.Edition == "lifetime"
                    ? "已激活 · 终身版"
                    : $"已激活 · {snapshot.Edition}",
                LicenseStatus.Trial =>
                    $"免费试用 · 剩余 {snapshot.DaysRemaining} 天（至 {snapshot.TrialEndsAt:yyyy-MM-dd}）",
                _ => "试用已结束 · 激活后可继续自动弹卡与记录生词",
            };
        }
    }

    public bool IsLicenseExpired => _license.Current.Status == LicenseStatus.TrialExpired;

    public string DataDirectory => AppPaths.RootDirectory;

    public string DataDirectoryText => $"数据目录：{DataDirectory}";

    public string SchedulerSummary => _schedulerStatus.DescribeStatus();

    [ObservableProperty]
    private string _librarySummary = "词库加载中…";

    [ObservableProperty]
    private string _pauseSummary = "提醒未暂停";

    [ObservableProperty]
    private string _statusMessage = "准备就绪。点击上方按钮手动弹出一张单词卡片。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private WordCard? _searchResult;

    [ObservableProperty]
    private string _searchStatus = "输入英文单词后按回车查询本地词库。";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    public bool HasSearchResult => SearchResult is not null;

    /// <summary>启动时载入词库统计。</summary>
    public void Initialize(int wordCount)
    {
        LibrarySummary = wordCount > 0
            ? $"本地词库：{wordCount:N0} 个单词（SQLite）"
            : "词库为空，请检查种子词库或执行导入。";
    }

    [RelayCommand(CanExecute = nameof(CanShowCard))]
    private async Task ShowCardAsync()
    {
        IsBusy = true;
        StatusMessage = "正在弹出单词卡片…";

        try
        {
            var result = await _wordCardService.ShowNextCardAsync(LearningSource.Manual);
            StatusMessage = DescribeResult(result);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "操作已取消。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动弹出单词卡片失败。");
            StatusMessage = "弹出卡片失败，详细信息已写入日志。";
        }
        finally
        {
            IsBusy = false;
            RefreshState();
        }
    }

    private bool CanShowCard() => !IsBusy && _license.CanUsePremiumFeatures;

    partial void OnIsBusyChanged(bool value) => ShowCardCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        IsSearching = true;
        SearchStatus = "查询中…";

        try
        {
            var card = await _wordLibrary.FindByWordAsync(query);
            SearchResult = card;

            SearchStatus = card is null
                ? $"未找到单词“{query}”。词库支持大小写不敏感精确查询。"
                : $"找到 {card.Definitions.Count} 条释义。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询单词失败：{Query}", query);
            SearchResult = null;
            SearchStatus = "查询失败，详细信息已写入日志。";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery);

    partial void OnSearchQueryChanged(string value) => SearchCommand.NotifyCanExecuteChanged();

    partial void OnIsSearchingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();

    partial void OnSearchResultChanged(WordCard? value) => OnPropertyChanged(nameof(HasSearchResult));

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportLibraryAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择词库文件",
            Filter = "词库文件 (*.json;*.csv)|*.json;*.csv|JSON 文件 (*.json)|*.json|CSV 文件 (*.csv)|*.csv",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var format = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? WordImportFormat.Csv
            : WordImportFormat.Json;

        IsImporting = true;
        ImportStatus = $"正在导入 {Path.GetFileName(dialog.FileName)}…";

        try
        {
            await using var stream = File.OpenRead(dialog.FileName);
            var result = await _importer.ImportAsync(stream, format);

            ImportStatus = $"导入完成：{result}";
            Initialize(await _wordLibrary.GetWordCountAsync());

            if (result.Errors.Count > 0)
            {
                ImportStatus += $"，首条错误：{result.Errors[0].Word} — {result.Errors[0].Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入词库失败：{File}", dialog.FileName);
            ImportStatus = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanImport() => !IsImporting;

    partial void OnIsImportingChanged(bool value) => ImportLibraryCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(DataDirectory);

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(LogDirectory);

    [RelayCommand]
    private void ActivateLicense() => _windowLauncher.ShowLicense();

    [RelayCommand]
    private void OpenSentencePractice()
    {
        if (!_license.CanUsePremiumFeatures)
        {
            _windowLauncher.ShowLicense();
            return;
        }

        _windowLauncher.ShowSentencePractice();
    }

    private void OnLicenseChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshLicenseState);
            return;
        }

        RefreshLicenseState();
    }

    private void RefreshLicenseState()
    {
        OnPropertyChanged(nameof(LicenseSummary));
        OnPropertyChanged(nameof(IsLicenseExpired));
        ShowCardCommand.NotifyCanExecuteChanged();
    }

    public string LogDirectory => AppPaths.LogDirectory;

    /// <summary>刷新暂停状态等易变信息（窗口激活或状态计时器触发时调用）。</summary>
    public void RefreshState()
    {
        PauseSummary = _pauseState.PausedUntil is { } until
            ? $"提醒已暂停至 {until:MM-dd HH:mm}"
            : "提醒未暂停";

        OnPropertyChanged(nameof(SchedulerSummary));
        OnPropertyChanged(nameof(LicenseSummary));
        OnPropertyChanged(nameof(IsLicenseExpired));
        ShowCardCommand.NotifyCanExecuteChanged();
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开目录失败：{Path}", path);
            StatusMessage = $"无法打开目录：{path}";
        }
    }

    private static string DescribeResult(WordCardResult? result) => result switch
    {
        null => "词库中没有可展示的单词。",
        WordCardResult.Recognized => "已记录：认识，下次复习已按间隔安排。",
        WordCardResult.Vague => "已记录：模糊，将按短间隔复习。",
        WordCardResult.Unknown => "已记录：不认识，将尽快再次学习。",
        WordCardResult.Snoozed => "已选择稍后提醒，未记录掌握状态。",
        WordCardResult.Timeout => "卡片超时关闭，未记录掌握状态。",
        WordCardResult.Dismissed => "卡片已关闭，未记录掌握状态。",
        _ => "卡片已关闭。",
    };
}
