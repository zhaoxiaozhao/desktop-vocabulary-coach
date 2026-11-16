using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Statistics;

namespace VocabularyCoach.App.ViewModels;

/// <summary>今日学习统计视图模型。</summary>
public sealed partial class StatisticsViewModel : ObservableObject
{
    private readonly IStatisticsService _statisticsService;
    private readonly ILogger<StatisticsViewModel> _logger;

    public StatisticsViewModel(
        IStatisticsService statisticsService,
        ILogger<StatisticsViewModel> logger)
    {
        _statisticsService = statisticsService;
        _logger = logger;
    }

    [ObservableProperty]
    private TodayStatistics _today = new(
        DateOnly.FromDateTime(DateTime.Now),
        TotalSessions: 0,
        Recognized: 0,
        Vague: 0,
        Unknown: 0,
        Dismissed: 0,
        Snoozed: 0,
        Timeout: 0,
        DueReviewCount: 0,
        WordsLearnedToday: 0);

    [ObservableProperty]
    private string _status = "加载中…";

    [ObservableProperty]
    private bool _isLoading;

    public string DateText => Today.Date.ToString("yyyy-MM-dd");

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task Refresh() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            Today = await _statisticsService.GetTodayAsync(CancellationToken.None);
            OnPropertyChanged(nameof(DateText));
            Status = $"更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载今日统计失败。");
            Status = "加载失败，详细信息已写入日志。";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
