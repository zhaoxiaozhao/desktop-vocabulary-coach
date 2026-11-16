using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.App.ViewModels;

public enum LearnerLevel
{
    Adaptive = 1,
    Beginner = 2,
    Intermediate = 3,
    Advanced = 4,
}

public enum ReminderPace
{
    Relaxed = 1,
    Standard = 2,
    Active = 3,
}

/// <summary>首次启动向导：英语水平、提醒节奏、勿扰时段、测试弹窗。</summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<FirstRunViewModel> _logger;

    public FirstRunViewModel(
        ISettingsService settingsService,
        ILogger<FirstRunViewModel> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        var settings = settingsService.Current;
        QuietHoursStart = settings.QuietHoursStart;
        QuietHoursEnd = settings.QuietHoursEnd;
        SelectedPace = ReminderPace.Standard;
    }

    public IReadOnlyList<EnumOption<LearnerLevel>> Levels { get; } =
    [
        new(LearnerLevel.Adaptive, "暂不确定，使用自适应"),
        new(LearnerLevel.Beginner, "初级（常用基础词）"),
        new(LearnerLevel.Intermediate, "中级（四六级/日常阅读）"),
        new(LearnerLevel.Advanced, "高级（雅思托福/专业阅读）"),
    ];

    public IReadOnlyList<EnumOption<ReminderPace>> Paces { get; } =
    [
        new(ReminderPace.Relaxed, "轻松：20–40 分钟一次"),
        new(ReminderPace.Standard, "标准：12–35 分钟一次"),
        new(ReminderPace.Active, "积极：5–15 分钟一次"),
    ];

    public event EventHandler<bool>? Finished;

    [ObservableProperty]
    private LearnerLevel _selectedLevel = LearnerLevel.Adaptive;

    [ObservableProperty]
    private ReminderPace _selectedPace = ReminderPace.Standard;

    [ObservableProperty]
    private string _quietHoursStart = "22:30";

    [ObservableProperty]
    private string _quietHoursEnd = "08:00";

    [ObservableProperty]
    private bool _showTestCard = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishAsync()
    {
        IsBusy = true;

        try
        {
            var settings = _settingsService.Current.Clone();

            settings.RemindersEnabled = true;
            settings.QuietHoursStart = QuietHoursStart;
            settings.QuietHoursEnd = QuietHoursEnd;

            switch (SelectedLevel)
            {
                case LearnerLevel.Beginner:
                    settings.DifficultyMode = DifficultyMode.Manual;
                    settings.ManualDifficulty = 1;
                    break;
                case LearnerLevel.Intermediate:
                    settings.DifficultyMode = DifficultyMode.Manual;
                    settings.ManualDifficulty = 2;
                    break;
                case LearnerLevel.Advanced:
                    settings.DifficultyMode = DifficultyMode.Manual;
                    settings.ManualDifficulty = 3;
                    break;
                default:
                    settings.DifficultyMode = DifficultyMode.Adaptive;
                    break;
            }

            (settings.MinIntervalMinutes, settings.MaxIntervalMinutes) = SelectedPace switch
            {
                ReminderPace.Relaxed => (20, 40),
                ReminderPace.Active => (5, 15),
                _ => (12, 35),
            };

            await _settingsService.SaveAsync(settings, CancellationToken.None);
            await _settingsService.MarkInitializedAsync(CancellationToken.None);

            Finished?.Invoke(this, ShowTestCard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存首次启动配置失败。");
            StatusMessage = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanFinish() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => FinishCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task SkipAsync()
    {
        try
        {
            await _settingsService.MarkInitializedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "标记首次启动完成失败。");
        }

        Finished?.Invoke(this, false);
    }
}
