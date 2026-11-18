using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 把译文粘贴回原应用：临时占用剪贴板，恢复目标窗口焦点，模拟 Ctrl+V，再恢复剪贴板。
/// 高权限窗口可能禁止模拟输入，此时调用方应提示用户改用「复制译文」。
/// </summary>
public sealed class SelectionReplacer
{
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly ILogger<SelectionReplacer> _logger;

    public SelectionReplacer(ILogger<SelectionReplacer> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ReplaceAsync(
        IntPtr targetWindow,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        IDataObject? previousData = null;
        try
        {
            previousData = Clipboard.GetDataObject();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取原剪贴板失败，替换后将无法恢复剪贴板。");
        }

        if (!TrySetClipboardText(text))
        {
            return false;
        }

        var replaced = false;

        try
        {
            if (targetWindow != IntPtr.Zero && IsWindow(targetWindow))
            {
                SetForegroundWindow(targetWindow);
                await Task.Delay(150, cancellationToken).ConfigureAwait(true);

                SendCtrlV();
                await Task.Delay(350, cancellationToken).ConfigureAwait(true);
                replaced = true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动粘贴译文失败。");
        }
        finally
        {
            if (previousData is not null)
            {
                try
                {
                    Clipboard.SetDataObject(previousData, true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "恢复剪贴板失败。");
                }
            }
        }

        return replaced;
    }

    public bool Copy(string text)
    {
        return TrySetClipboardText(text);
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    private static void SendCtrlV()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}
