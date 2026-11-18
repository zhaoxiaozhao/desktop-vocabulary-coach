using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VocabularyCoach.App.Views;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 中文转英文编排：读取选区 → AI 翻译 → 预览确认 → 原位替换或复制。
/// </summary>
public sealed class SelectionTranslationCoordinator
{
    private const int MaxPreviewCharacters = 4000;

    private readonly ISelectedTextReader _selectedTextReader;
    private readonly ITextTranslationService _translation;
    private readonly ILicenseService _license;
    private readonly SelectionReplacer _replacer;
    private readonly TrayIconService _tray;
    private readonly ILogger<SelectionTranslationCoordinator> _logger;

    private TranslationPreviewWindow? _window;
    private bool _isBusy;

    public SelectionTranslationCoordinator(
        ISelectedTextReader selectedTextReader,
        ITextTranslationService translation,
        ILicenseService license,
        SelectionReplacer replacer,
        TrayIconService tray,
        ILogger<SelectionTranslationCoordinator> logger)
    {
        _selectedTextReader = selectedTextReader;
        _translation = translation;
        _license = license;
        _replacer = replacer;
        _tray = tray;
        _logger = logger;
    }

    public async Task TranslateSelectionAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!_license.CanUsePremiumFeatures)
        {
            _tray.ShowInfo("试用已结束", "激活许可证后可继续使用中文转英文。");
            return;
        }

        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _isBusy = true;

        try
        {
            // 在打开任何窗口前记录目标窗口，替换时再切回去。
            var targetWindow = GetForegroundWindow();

            var rawText = await _selectedTextReader
                .GetSelectedTextAsync(CancellationToken.None)
                .ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(rawText))
            {
                _tray.ShowInfo(
                    "中文转英文",
                    "未读取到选中文字。请先在目标应用中选中中文，再按 Ctrl+Alt+T。");
                return;
            }

            if (!TextTranslationText.ContainsChinese(rawText))
            {
                _tray.ShowInfo("中文转英文", "选中的内容不是中文，无需翻译。");
                return;
            }

            if (rawText.Length > MaxPreviewCharacters)
            {
                _tray.ShowInfo(
                    "中文转英文",
                    $"选中的文字太长（{rawText.Length} 字），请控制在 {MaxPreviewCharacters} 字以内。");
                return;
            }

            var result = await _translation
                .TranslateAsync(rawText, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                _tray.ShowInfo("中文转英文失败", result.Message);
                return;
            }

            ShowPreview(targetWindow, rawText, result.Translation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "中文转英文失败。");
            _tray.ShowInfo("中文转英文失败", "发生错误，详细信息已写入日志。");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ShowPreview(IntPtr targetWindow, string original, string translation)
    {
        var window = new TranslationPreviewWindow(original, translation);
        _window = window;

        window.ReplaceRequested += async text =>
        {
            window.SetStatus("正在替换…");
            var replaced = await _replacer
                .ReplaceAsync(targetWindow, text, CancellationToken.None)
                .ConfigureAwait(true);

            if (!replaced)
            {
                _replacer.Copy(text);
            }

            window.Close();
            _tray.ShowInfo(
                replaced ? "已替换为英文" : "已复制译文",
                replaced
                    ? "原选区已替换。若结果不对，可在目标应用中按 Ctrl+Z 撤销。"
                    : "目标应用不允许自动粘贴，译文已复制，请手动粘贴。");
        };

        window.CopyRequested += text =>
        {
            if (_replacer.Copy(text))
            {
                _tray.ShowInfo("已复制译文", "可以到目标应用中手动粘贴。");
            }
            else
            {
                window.SetStatus("复制失败，剪贴板可能被其他程序占用，请重试。");
                return;
            }

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
