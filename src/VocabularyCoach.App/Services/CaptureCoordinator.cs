using Microsoft.Extensions.Logging;
using VocabularyCoach.App.ViewModels;
using VocabularyCoach.App.Views;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Capture;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 生词捕获编排：读取选区 → 静默加入或弹确认窗。
/// </summary>
public sealed class CaptureCoordinator
{
    private readonly ISelectedTextReader _selectedTextReader;
    private readonly IWordCaptureService _captureService;
    private readonly ISettingsService _settings;
    private readonly TrayIconService _tray;
    private readonly ILicenseService _license;
    private readonly WindowLauncher _windowLauncher;
    private readonly ILogger<CaptureWordViewModel> _viewModelLogger;
    private readonly ILogger<CaptureCoordinator> _logger;

    private CaptureWordWindow? _window;

    public CaptureCoordinator(
        ISelectedTextReader selectedTextReader,
        IWordCaptureService captureService,
        ISettingsService settings,
        TrayIconService tray,
        ILicenseService license,
        WindowLauncher windowLauncher,
        ILogger<CaptureWordViewModel> viewModelLogger,
        ILogger<CaptureCoordinator> logger)
    {
        _selectedTextReader = selectedTextReader;
        _captureService = captureService;
        _settings = settings;
        _tray = tray;
        _license = license;
        _windowLauncher = windowLauncher;
        _viewModelLogger = viewModelLogger;
        _logger = logger;
    }

    /// <summary>热键触发：读取当前选区并捕获。</summary>
    public async Task CaptureFromSelectionAsync()
    {
        if (!_license.CanUsePremiumFeatures)
        {
            _tray.ShowInfo("试用已结束", "激活许可证后可继续记录生词。");
            _windowLauncher.ShowLicense();
            return;
        }

        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        try
        {
            var rawText = await _selectedTextReader.GetSelectedTextAsync(CancellationToken.None);

            if (string.IsNullOrWhiteSpace(rawText))
            {
                ShowWindow(
                    preview: null,
                    hint: "未读取到选中文字。请手动输入英文单词或短语，或先在其他应用中选中文字后重试。");
                return;
            }

            var preview = await _captureService.InspectAsync(rawText, CancellationToken.None);
            var settings = _settings.Current;

            if (settings.CaptureSilentAdd &&
                preview.IsValid &&
                preview.ExistsInLibrary &&
                !preview.AlreadyLearning &&
                !string.IsNullOrWhiteSpace(preview.SuggestedDefinition))
            {
                var result = await _captureService.AddAsync(rawText, null, CancellationToken.None);
                Notify(result);
                return;
            }

            ShowWindow(preview, hint: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "捕获生词失败。");
            _tray.ShowInfo("捕获失败", "无法读取选区，请手动重试（详细信息已写入日志）。");
        }
    }

    /// <summary>托盘菜单触发：直接打开手动输入窗口。</summary>
    public void CaptureManually()
    {
        if (!_license.CanUsePremiumFeatures)
        {
            _tray.ShowInfo("试用已结束", "激活许可证后可继续记录生词。");
            _windowLauncher.ShowLicense();
            return;
        }

        ShowWindow(
            preview: null,
            hint: "请手动输入要记录的英文单词或短语（支持 1–5 个单词）。");
    }

    private void ShowWindow(CapturePreview? preview, string? hint)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        preview ??= new CapturePreview(
            IsValid: false,
            Error: hint,
            OriginalText: string.Empty,
            NormalizedWord: string.Empty,
            ExistsInLibrary: false,
            AlreadyLearning: false,
            Card: null,
            SuggestedDefinition: null);

        var viewModel = new CaptureWordViewModel(
            preview,
            _captureService,
            _settings,
            _viewModelLogger);

        var window = new CaptureWordWindow(viewModel);
        _window = window;

        viewModel.Completed += (_, result) =>
        {
            Notify(result);
            window.Close();
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };

        window.Show();
        window.Activate();
    }

    private void Notify(CaptureResult result)
    {
        if (!_settings.Current.CaptureShowNotification)
        {
            return;
        }

        var title = result.Outcome == CaptureOutcome.AlreadyLearning ? "已在生词本" : "已记录生词";
        _tray.ShowInfo(title, result.Message ?? result.Word);
    }
}
