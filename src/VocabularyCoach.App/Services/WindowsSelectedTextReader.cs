using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 读取其他应用中的选中文字：
/// 1. 优先使用 UI Automation 的 TextPattern（不修改剪贴板）；
/// 2. 失败时模拟 Ctrl+C 读取剪贴板，并恢复原剪贴板内容。
/// 整个过程不记录选中文本。
/// </summary>
public sealed class WindowsSelectedTextReader : ISelectedTextReader
{
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12;
    private const byte VkShift = 0x10;
    private const byte VkC = 0x43;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly ILogger<WindowsSelectedTextReader> _logger;

    public WindowsSelectedTextReader(ILogger<WindowsSelectedTextReader> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken)
    {
        var automationText = TryReadWithUiAutomation();
        if (!string.IsNullOrWhiteSpace(automationText))
        {
            return automationText;
        }

        return await TryReadWithClipboardAsync(cancellationToken).ConfigureAwait(true);
    }

    private string? TryReadWithUiAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern textPattern)
            {
                return null;
            }

            var ranges = textPattern.GetSelection();
            if (ranges.Length == 0)
            {
                return null;
            }

            var text = string.Concat(ranges.Select(range => range.GetText(-1)));
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UI Automation 读取选区失败，尝试剪贴板回退。");
            return null;
        }
    }

    private async Task<string?> TryReadWithClipboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(foreground, out var processId);
            if (processId == (uint)Environment.ProcessId)
            {
                // 热键没有激活我们的窗口时不应走到这里；避免对自己模拟复制。
                return null;
            }

            // 等待用户松开 Ctrl/Alt/Shift，避免 Ctrl+Alt+W 的修饰键污染 Ctrl+C。
            var releaseDeadline = Environment.TickCount64 + 600;
            while (Environment.TickCount64 < releaseDeadline)
            {
                if (!IsKeyDown(VkControl) && !IsKeyDown(VkMenu) && !IsKeyDown(VkShift))
                {
                    break;
                }

                await Task.Delay(20, cancellationToken).ConfigureAwait(true);
            }

            IDataObject? previousData = null;
            string? previousText = null;
            try
            {
                previousData = Clipboard.GetDataObject();
                previousText = TryGetClipboardText();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取原剪贴板失败，将只尝试获取选区。");
            }

            var sequenceBeforeCopy = GetClipboardSequenceNumber();
            SendCtrlC();

            var captured = await WaitForClipboardTextAsync(previousText, sequenceBeforeCopy, cancellationToken).ConfigureAwait(true);

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

            return captured;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "通过剪贴板读取选区失败。");
            return null;
        }
    }

    private static async Task<string?> WaitForClipboardTextAsync(
        string? previousText,
        uint sequenceBeforeCopy,
        CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + 800;

        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = TryGetClipboardText();
            var sequenceChanged = GetClipboardSequenceNumber() != sequenceBeforeCopy;

            // 选区内容可能与剪贴板原有内容相同；此时序列号仍会变化，也应视为复制成功。
            if (!string.IsNullOrWhiteSpace(text) &&
                (sequenceChanged || !string.Equals(text, previousText, StringComparison.Ordinal)))
            {
                return text.Trim();
            }

            await Task.Delay(30, cancellationToken).ConfigureAwait(true);
        }

        return null;
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static void SendCtrlC()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static bool IsKeyDown(byte virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
