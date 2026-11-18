using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VocabularyCoach.App.Services;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Backup;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Infrastructure.Diagnostics;

namespace VocabularyCoach.App.ViewModels;

public sealed record EnumOption<T>(T Value, string Label);

/// <summary>设置界面视图模型：编辑、保存、备份与恢复。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseBackupService _backupService;
    private readonly ThemeService _themeService;
    private readonly VocabularyCoach.Application.Adaptive.IAdaptiveDifficultyService _adaptiveDifficulty;
    private readonly IAiEnrichmentService _aiEnrichment;
    private readonly IAiKeyStore _keyStore;
    private readonly VocabularyCoach.Application.Abstractions.ISystemStartupManager _startupManager;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly List<AiConnection> _connections = [];
    private bool _isLoadingConnections;
    private string _editingConnectionId = string.Empty;

    public SettingsViewModel(
        ISettingsService settingsService,
        IDatabaseBackupService backupService,
        ThemeService themeService,
        VocabularyCoach.Application.Adaptive.IAdaptiveDifficultyService adaptiveDifficulty,
        IAiEnrichmentService aiEnrichment,
        IAiKeyStore keyStore,
        VocabularyCoach.Application.Abstractions.ISystemStartupManager startupManager,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _backupService = backupService;
        _themeService = themeService;
        _adaptiveDifficulty = adaptiveDifficulty;
        _aiEnrichment = aiEnrichment;
        _keyStore = keyStore;
        _startupManager = startupManager;
        _logger = logger;

        RecommendationModes =
        [
            new(RecommendationMode.Smart, "Smart：新词与到期复习混合"),
            new(RecommendationMode.NewWordsOnly, "仅新词"),
            new(RecommendationMode.ReviewOnly, "仅复习"),
        ];

        DifficultyModes =
        [
            new(DifficultyMode.Adaptive, "自适应（根据最近反馈调整）"),
            new(DifficultyMode.Manual, "手动固定难度"),
        ];

        InteractionModes =
        [
            new(InteractionMode.SelfReport, "自我评估：认识 / 模糊 / 不认识"),
            new(InteractionMode.ChoiceQuiz, "看词选义：4 选 1"),
            new(InteractionMode.ReverseQuiz, "看义选词：根据中文选英文"),
            new(InteractionMode.Mixed, "混合：自我评估 + 正/反选择"),
        ];

        Themes =
        [
            new(CardTheme.Light, "浅色"),
            new(CardTheme.Dark, "深色"),
        ];

        AiPresets = AiPresetCatalog.All
            .Select(p => new EnumOption<AiPreset>(p.Preset, p.Name))
            .ToList();

        LoadFrom(_settingsService.Current);
    }

    public event EventHandler? CloseRequested;

    public event EventHandler? Saved;

    public IReadOnlyList<EnumOption<AiPreset>> AiPresets { get; }

    public IReadOnlyList<EnumOption<RecommendationMode>> RecommendationModes { get; }

    public IReadOnlyList<EnumOption<DifficultyMode>> DifficultyModes { get; }

    public IReadOnlyList<EnumOption<InteractionMode>> InteractionModes { get; }

    public IReadOnlyList<EnumOption<CardTheme>> Themes { get; }

    public IReadOnlyList<int> DifficultyLevels { get; } = [1, 2, 3, 4];

    public string DataDirectory => AppPaths.RootDirectory;

    [ObservableProperty]
    private bool _remindersEnabled;

    [ObservableProperty]
    private double _minIntervalMinutes;

    [ObservableProperty]
    private double _maxIntervalMinutes;

    [ObservableProperty]
    private int _maxPopupsPerHour;

    [ObservableProperty]
    private string _quietHoursStart = "22:30";

    [ObservableProperty]
    private string _quietHoursEnd = "08:00";

    [ObservableProperty]
    private double _idleThresholdMinutes;

    [ObservableProperty]
    private string _pauseUntilTomorrowTime = "00:00";

    [ObservableProperty]
    private bool _enableIdleDetection;

    [ObservableProperty]
    private bool _detectFullscreen;

    [ObservableProperty]
    private RecommendationMode _recommendationMode;

    [ObservableProperty]
    private DifficultyMode _difficultyMode;

    [ObservableProperty]
    private InteractionMode _interactionMode;

    [ObservableProperty]
    private bool _captureEnabled;

    [ObservableProperty]
    private bool _captureSilentAdd;

    [ObservableProperty]
    private bool _captureShowNotification;

    [ObservableProperty]
    private bool _chineseToEnglishEnabled;

    [ObservableProperty]
    private bool _chineseToEnglishPreviewEnabled;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private int _manualDifficulty;

    [ObservableProperty]
    private double _newWordRatio;

    [ObservableProperty]
    private CardTheme _theme;

    [ObservableProperty]
    private bool _showPhonetic;

    [ObservableProperty]
    private bool _autoExpand;

    [ObservableProperty]
    private bool _animate;

    [ObservableProperty]
    private bool _aiEnabled;

    [ObservableProperty]
    private AiPreset _selectedAiPreset = AiPreset.OpenAi;

    [ObservableProperty]
    private string _aiBaseUrl = string.Empty;

    [ObservableProperty]
    private string _aiModel = string.Empty;

    [ObservableProperty]
    private string _sentencePracticeModel = string.Empty;

    [ObservableProperty]
    private bool _sentenceGenerationEnabled = true;

    [ObservableProperty]
    private int _sentenceGenerationRunsPerDay = 2;

    [ObservableProperty]
    private int _sentenceGenerationBatchSize = 5;

    [ObservableProperty]
    private int _sentenceGenerationIdleMinutes = 5;

    public ObservableCollection<AiConnectionOption> AiConnectionOptions { get; } = [];

    [ObservableProperty]
    private string _selectedAiConnectionId = string.Empty;

    [ObservableProperty]
    private string _selectedAiConnectionName = string.Empty;

    [ObservableProperty]
    private bool _aiAutoMode;

    [ObservableProperty]
    private bool _selectedAiConnectionEnabled = true;

    [ObservableProperty]
    private string _apiKeyStatus = "未设置";

    [ObservableProperty]
    private string _aiTestStatus = string.Empty;

    [ObservableProperty]
    private bool _isTestingAi;

    /// <summary>由 PasswordBox 输入，仅在保存时写入 DPAPI 存储。</summary>
    public string PendingApiKey { get; set; } = string.Empty;

    public bool ClearApiKeyRequested { get; private set; }

    [ObservableProperty]
    private string _statusMessage = "修改设置后点击“保存并应用”。";

    [ObservableProperty]
    private bool _isBusy;

    private void LoadFrom(UserSettings settings)
    {
        RemindersEnabled = settings.RemindersEnabled;
        MinIntervalMinutes = settings.MinIntervalMinutes;
        MaxIntervalMinutes = settings.MaxIntervalMinutes;
        MaxPopupsPerHour = settings.MaxPopupsPerHour;
        QuietHoursStart = settings.QuietHoursStart;
        QuietHoursEnd = settings.QuietHoursEnd;
        IdleThresholdMinutes = settings.IdleThresholdMinutes;
        EnableIdleDetection = settings.EnableIdleDetection;
        DetectFullscreen = settings.DetectFullscreen;
        PauseUntilTomorrowTime = settings.PauseUntilTomorrowTime;
        RecommendationMode = settings.RecommendationMode;
        DifficultyMode = settings.DifficultyMode;
        InteractionMode = settings.InteractionMode;
        CaptureEnabled = settings.CaptureEnabled;
        CaptureSilentAdd = settings.CaptureSilentAdd;
        CaptureShowNotification = settings.CaptureShowNotification;
        ChineseToEnglishEnabled = settings.ChineseToEnglishEnabled;
        ChineseToEnglishPreviewEnabled = settings.ChineseToEnglishPreviewEnabled;
        StartWithWindows = settings.StartWithWindows;
        ManualDifficulty = settings.ManualDifficulty;
        NewWordRatio = settings.NewWordRatio;
        Theme = settings.Theme;
        ShowPhonetic = settings.ShowPhonetic;
        AutoExpand = settings.AutoExpand;
        Animate = settings.Animate;
        AiEnabled = settings.AiEnabled;
        AiAutoMode = settings.AiAutoMode;

        AiConnectionSettings.Ensure(settings);
        _connections.Clear();
        _connections.AddRange(settings.AiConnections.Select(x => x.Clone()));
        RefreshConnectionOptions();

        _isLoadingConnections = true;
        SelectedAiConnectionId = settings.ActiveAiConnectionId;
        _editingConnectionId = SelectedAiConnectionId;
        _isLoadingConnections = false;

        var active = SelectedConnection;
        if (active is not null)
        {
            LoadConnectionFields(active);
        }

        SentenceGenerationEnabled = settings.SentenceGenerationEnabled;
        SentenceGenerationRunsPerDay = settings.SentenceGenerationRunsPerDay;
        SentenceGenerationBatchSize = settings.SentenceGenerationBatchSize;
        SentenceGenerationIdleMinutes = settings.SentenceGenerationIdleMinutes;
    }

    private void RefreshConnectionOptions()
    {
        AiConnectionOptions.Clear();
        foreach (var connection in _connections)
        {
            var suffix = connection.Enabled ? string.Empty : "（停用）";
            AiConnectionOptions.Add(new AiConnectionOption(
                connection.Id,
                connection.Name + suffix));
        }
    }

    private AiConnection? SelectedConnection =>
        _connections.FirstOrDefault(x => string.Equals(
            x.Id,
            SelectedAiConnectionId,
            StringComparison.OrdinalIgnoreCase));

    private void LoadConnectionFields(AiConnection connection)
    {
        SelectedAiConnectionName = connection.Name;
        SelectedAiPreset = connection.Preset;
        AiBaseUrl = connection.BaseUrl;
        AiModel = connection.Model;
        SentencePracticeModel = connection.SentencePracticeModel;
        SelectedAiConnectionEnabled = connection.Enabled;
    }

    private void CommitCurrentFields()
    {
        var connection = _connections.FirstOrDefault(x => string.Equals(
            x.Id,
            _editingConnectionId,
            StringComparison.OrdinalIgnoreCase));

        if (connection is null)
        {
            return;
        }

        connection.Preset = SelectedAiPreset;
        connection.BaseUrl = AiBaseUrl.Trim();
        connection.Model = AiModel.Trim();
        connection.SentencePracticeModel = SentencePracticeModel.Trim();
        connection.Enabled = SelectedAiConnectionEnabled;
        connection.Name = string.IsNullOrWhiteSpace(SelectedAiConnectionName)
            ? connection.Name
            : SelectedAiConnectionName.Trim();
    }

    partial void OnSelectedAiConnectionIdChanged(string value)
    {
        if (_isLoadingConnections)
        {
            return;
        }

        CommitCurrentFields();
        _editingConnectionId = value;

        var connection = SelectedConnection;
        if (connection is null)
        {
            return;
        }

        LoadConnectionFields(connection);
        PendingApiKey = string.Empty;
        ClearApiKeyRequested = false;
        _ = RefreshApiKeyStatusAsync();
    }

    [RelayCommand]
    private void AddAiConnection()
    {
        CommitCurrentFields();

        var preset = AiPresetCatalog.Get(AiPreset.OpenAi);
        var connection = new AiConnection
        {
            Name = $"连接 {_connections.Count + 1}",
            Preset = AiPreset.OpenAi,
            BaseUrl = preset.BaseUrl,
            Model = preset.DefaultModel,
            Enabled = true,
        };

        _connections.Add(connection);
        RefreshConnectionOptions();
        SelectedAiConnectionId = connection.Id;
    }

    [RelayCommand]
    private void DeleteAiConnection()
    {
        if (_connections.Count <= 1)
        {
            return;
        }

        CommitCurrentFields();
        var selected = SelectedConnection;
        if (selected is null)
        {
            return;
        }

        _connections.Remove(selected);
        _ = _keyStore.DeleteAsync(selected.Id, CancellationToken.None);
        RefreshConnectionOptions();
        SelectedAiConnectionId = _connections[0].Id;
    }

    /// <summary>窗口加载后刷新 API Key 状态（只显示是否已保存）。</summary>
    public async Task InitializeAsync()
    {
        await RefreshApiKeyStatusAsync();
    }

    private async Task RefreshApiKeyStatusAsync()
    {
        try
        {
            var connectionId = SelectedAiConnectionId;
            var key = string.IsNullOrWhiteSpace(connectionId)
                ? null
                : await _keyStore.GetAsync(connectionId, CancellationToken.None);

            ApiKeyStatus = string.IsNullOrWhiteSpace(key)
                ? "未设置 API Key（Mock 模式不需要）"
                : "已保存 API Key（加密存储，不可查看）";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 API Key 状态失败。");
            ApiKeyStatus = "无法读取已保存的 Key";
        }
    }

    partial void OnSelectedAiPresetChanged(AiPreset value)
    {
        if (value is AiPreset.Custom or AiPreset.Mock)
        {
            return;
        }

        var preset = AiPresetCatalog.Get(value);
        AiBaseUrl = preset.BaseUrl;
        AiModel = preset.DefaultModel;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsBusy = true;
        var keySaved = true;

        try
        {
            var selectedConnection = SelectedConnection;
            if (selectedConnection is not null)
            {
                selectedConnection.Preset = SelectedAiPreset;
                selectedConnection.BaseUrl = AiBaseUrl.Trim();
                selectedConnection.Model = AiModel.Trim();
                selectedConnection.SentencePracticeModel = SentencePracticeModel.Trim();
                selectedConnection.Enabled = SelectedAiConnectionEnabled;
                if (!string.IsNullOrWhiteSpace(SelectedAiConnectionName))
                {
                    selectedConnection.Name = SelectedAiConnectionName.Trim();
                }
            }

            var settings = new UserSettings
            {
                RemindersEnabled = RemindersEnabled,
                MinIntervalMinutes = MinIntervalMinutes,
                MaxIntervalMinutes = MaxIntervalMinutes,
                MaxPopupsPerHour = MaxPopupsPerHour,
                QuietHoursStart = QuietHoursStart,
                QuietHoursEnd = QuietHoursEnd,
                IdleThresholdMinutes = IdleThresholdMinutes,
                EnableIdleDetection = EnableIdleDetection,
                DetectFullscreen = DetectFullscreen,
                PauseUntilTomorrowTime = PauseUntilTomorrowTime,
                RecommendationMode = RecommendationMode,
                DifficultyMode = DifficultyMode,
                InteractionMode = InteractionMode,
                CaptureEnabled = CaptureEnabled,
                CaptureSilentAdd = CaptureSilentAdd,
                CaptureShowNotification = CaptureShowNotification,
                ChineseToEnglishEnabled = ChineseToEnglishEnabled,
                ChineseToEnglishPreviewEnabled = ChineseToEnglishPreviewEnabled,
                StartWithWindows = StartWithWindows,
                ManualDifficulty = ManualDifficulty,
                NewWordRatio = NewWordRatio,
                Theme = Theme,
                ShowPhonetic = ShowPhonetic,
                AutoExpand = AutoExpand,
                Animate = Animate,
                AiEnabled = AiEnabled,
                AiPreset = SelectedAiPreset,
                AiBaseUrl = AiBaseUrl,
                AiModel = AiModel,
                SentencePracticeModel = SentencePracticeModel.Trim(),
                SentenceGenerationEnabled = SentenceGenerationEnabled,
                SentenceGenerationRunsPerDay = Math.Clamp(SentenceGenerationRunsPerDay, 1, 6),
                SentenceGenerationBatchSize = Math.Clamp(SentenceGenerationBatchSize, 1, 10),
                SentenceGenerationIdleMinutes = Math.Clamp(SentenceGenerationIdleMinutes, 1, 120),
                AiConnections = _connections.Select(x => x.Clone()).ToList(),
                ActiveAiConnectionId = selectedConnection?.Id ?? SelectedAiConnectionId,
                AiAutoMode = AiAutoMode,
            };

            await _settingsService.SaveAsync(settings, CancellationToken.None);

            try
            {
                await _adaptiveDifficulty.ResetPreferenceAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清除自适应比例偏好失败，不影响本次设置保存。");
            }

            try
            {
                var keyConnectionId = selectedConnection?.Id ?? SelectedAiConnectionId;
                if (!string.IsNullOrWhiteSpace(PendingApiKey)
                    && !string.IsNullOrWhiteSpace(keyConnectionId))
                {
                    await _keyStore.SetAsync(keyConnectionId, PendingApiKey, CancellationToken.None);
                }

                if (ClearApiKeyRequested
                    && !string.IsNullOrWhiteSpace(keyConnectionId))
                {
                    await _keyStore.DeleteAsync(keyConnectionId, CancellationToken.None);
                }

                PendingApiKey = string.Empty;
                ClearApiKeyRequested = false;
                await RefreshApiKeyStatusAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存 AI API Key 失败。");
                keySaved = false;
            }

            // 刷新下拉框显示的名称与「停用」后缀，同时保持当前选中项。
            var activeId = selectedConnection?.Id ?? SelectedAiConnectionId;
            RefreshConnectionOptions();
            if (!string.IsNullOrWhiteSpace(activeId))
            {
                SelectedAiConnectionId = activeId;
            }

            _themeService.Apply(Theme);

            var startupError = false;
            try
            {
                _startupManager.SetEnabled(StartWithWindows);
            }
            catch (Exception ex)
            {
                startupError = true;
                _logger.LogError(ex, "更新开机启动设置失败。");
            }

            StatusMessage = keySaved && !startupError
                ? "设置已保存并立即生效。"
                : "设置已保存，但部分系统设置失败（API Key / 开机启动），详见日志。";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存设置失败。");
            StatusMessage = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task BackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "备份学习数据库",
            Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            FileName = $"vocabulary-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _backupService.BackupAsync(dialog.FileName, CancellationToken.None);
            StatusMessage = $"备份完成：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "备份数据库失败。");
            StatusMessage = $"备份失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择数据库备份",
            Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "恢复会覆盖当前学习数据，完成后应用将退出，需要重新启动。\n\n是否继续？",
            "恢复数据库",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _backupService.RestoreAsync(dialog.FileName, CancellationToken.None);
            System.Windows.MessageBox.Show(
                "恢复完成。应用将退出，请重新启动以加载恢复后的数据。",
                "恢复数据库",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (System.Windows.Application.Current is App app)
            {
                app.RequestExit();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复数据库失败。");
            StatusMessage = $"恢复失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开数据目录失败。");
            StatusMessage = $"无法打开目录：{DataDirectory}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanTestAi))]
    private async Task TestAiAsync()
    {
        IsTestingAi = true;
        AiTestStatus = "正在测试连接…";

        try
        {
            var connection = new AiConnection
            {
                Id = SelectedAiConnectionId,
                Name = SelectedConnection?.Name ?? "当前连接",
                Preset = SelectedAiPreset,
                BaseUrl = AiBaseUrl.Trim(),
                Model = AiModel.Trim(),
                SentencePracticeModel = SentencePracticeModel.Trim(),
                Enabled = SelectedAiConnectionEnabled,
            };

            var result = await _aiEnrichment.TestAsync(
                string.IsNullOrWhiteSpace(PendingApiKey) ? null : PendingApiKey,
                connection,
                CancellationToken.None);

            AiTestStatus = result.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试 AI 连接失败。");
            AiTestStatus = "测试失败，详细信息已写入日志。";
        }
        finally
        {
            IsTestingAi = false;
        }
    }

    private bool CanTestAi() => !IsTestingAi;

    partial void OnIsTestingAiChanged(bool value) => TestAiCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void ClearApiKey()
    {
        ClearApiKeyRequested = true;
        ApiKeyStatus = "保存设置后将清除已保存的 API Key";
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
