using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Capture;

namespace VocabularyCoach.App.ViewModels;

/// <summary>生词本窗口视图模型。</summary>
public sealed partial class CapturedWordsViewModel : ObservableObject
{
    private readonly IWordCaptureService _captureService;
    private readonly ILogger<CapturedWordsViewModel> _logger;

    public CapturedWordsViewModel(
        IWordCaptureService captureService,
        ILogger<CapturedWordsViewModel> logger)
    {
        _captureService = captureService;
        _logger = logger;
    }

    [ObservableProperty]
    private IReadOnlyList<CapturedWord> _items = [];

    [ObservableProperty]
    private string _status = "加载中…";

    [ObservableProperty]
    private bool _isLoading;

    public bool IsEmpty => Items.Count == 0;

    partial void OnItemsChanged(IReadOnlyList<CapturedWord> value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;

        try
        {
            Items = await _captureService.GetCapturedAsync(500, CancellationToken.None);
            Status = Items.Count == 0
                ? "还没有捕获过生词。选中文字后按 Ctrl+Alt+W 试试。"
                : $"共 {Items.Count} 条；捕获的词会自动进入复习队列。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载生词本失败。");
            Status = "加载失败，详细信息已写入日志。";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
