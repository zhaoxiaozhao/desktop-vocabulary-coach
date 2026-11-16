using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Capture;
using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.App.ViewModels;

/// <summary>生词捕获确认窗口视图模型。</summary>
public sealed partial class CaptureWordViewModel : ObservableObject
{
    private readonly IWordCaptureService _captureService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CaptureWordViewModel> _logger;

    public CaptureWordViewModel(
        CapturePreview preview,
        IWordCaptureService captureService,
        ISettingsService settingsService,
        ILogger<CaptureWordViewModel> logger)
    {
        _captureService = captureService;
        _settingsService = settingsService;
        _logger = logger;

        WordText = preview.OriginalText;
        Phonetic = preview.Card?.DisplayPhonetic;
        IsFound = preview.ExistsInLibrary;
        IsAlreadyLearning = preview.AlreadyLearning;
        ShowDefinitionInput = preview.IsValid && !preview.ExistsInLibrary;
        SuggestedDefinition = preview.SuggestedDefinition;
        DefinitionZh = preview.SuggestedDefinition ?? string.Empty;
        SilentAdd = settingsService.Current.CaptureSilentAdd;
        StatusMessage = preview.IsValid
            ? preview.AlreadyLearning
                ? "该单词已在生词本或复习计划中，再次记录只会更新捕获时间。"
                : preview.ExistsInLibrary
                    ? "词库已收录，确认后加入生词本并立即安排复习。"
                    : "词库未收录这个单词/短语，请填写中文释义。"
            : preview.Error ?? "请手动输入英文单词或短语。";
    }

    public event EventHandler<CaptureResult>? Completed;

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private string _wordText;

    [ObservableProperty]
    private string _definitionZh;

    [ObservableProperty]
    private string? _phonetic;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _isFound;

    [ObservableProperty]
    private bool _isAlreadyLearning;

    [ObservableProperty]
    private bool _showDefinitionInput;

    [ObservableProperty]
    private string? _suggestedDefinition;

    [ObservableProperty]
    private bool _silentAdd;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        IsBusy = true;

        try
        {
            var definition = ShowDefinitionInput ? DefinitionZh : null;
            var result = await _captureService.AddAsync(WordText, definition, CancellationToken.None);

            StatusMessage = result.Message ?? string.Empty;

            switch (result.Outcome)
            {
                case CaptureOutcome.NeedsDefinition:
                    ShowDefinitionInput = true;
                    return;

                case CaptureOutcome.Invalid:
                    return;

                case CaptureOutcome.Added:
                case CaptureOutcome.AlreadyLearning:
                    await SaveSilentSettingAsync();
                    Completed?.Invoke(this, result);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存生词失败。");
            StatusMessage = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAdd() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => AddCommand.NotifyCanExecuteChanged();

    partial void OnWordTextChanged(string value)
    {
        // 用户修改文本后不再沿用旧的词库信息，保存时会重新校验。
        IsFound = false;
        IsAlreadyLearning = false;
        ShowDefinitionInput = true;
        SuggestedDefinition = null;
        DefinitionZh = string.Empty;
        Phonetic = null;
        StatusMessage = "已修改单词/短语，请确认释义后加入。";
    }

    private async Task SaveSilentSettingAsync()
    {
        if (SilentAdd == _settingsService.Current.CaptureSilentAdd)
        {
            return;
        }

        try
        {
            var settings = _settingsService.Current.Clone();
            settings.CaptureSilentAdd = SilentAdd;
            await _settingsService.SaveAsync(settings, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存静默捕获设置失败。");
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
