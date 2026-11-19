using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace VocabularyCoach.App.Services;

/// <summary>全局热键：生词捕获与中文转英文，组合键可在设置中修改。</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5643;
    private const int TranslateHotkeyId = 0x5644;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly ILogger<GlobalHotkeyService> _logger;
    private HwndSource? _source;
    private HotkeyGesture? _captureGesture;
    private HotkeyGesture? _translateGesture;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? Pressed;

    public event EventHandler? TranslatePressed;

    public bool IsRegistered { get; private set; }

    public bool IsTranslateRegistered { get; private set; }

    public bool TryRegister(string? hotkeyText)
    {
        if (!HotkeyGesture.TryParse(hotkeyText, out var gesture))
        {
            _logger.LogWarning("生词捕获热键格式无效：{Hotkey}", hotkeyText);
            return false;
        }

        if (IsRegistered && _captureGesture == gesture)
        {
            return true;
        }

        Unregister();

        try
        {
            EnsureSource();

            IsRegistered = RegisterHotKey(
                _source!.Handle,
                HotkeyId,
                gesture.Modifiers | ModNoRepeat,
                gesture.VirtualKey);

            if (IsRegistered)
            {
                _captureGesture = gesture;
            }
            else
            {
                _logger.LogWarning(
                    "全局热键 {Hotkey} 注册失败，可能已被其他程序占用。",
                    gesture.DisplayText);
            }

            return IsRegistered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册全局热键失败。");
            return false;
        }
    }

    public bool TryRegisterTranslate(string? hotkeyText)
    {
        if (!HotkeyGesture.TryParse(hotkeyText, out var gesture))
        {
            _logger.LogWarning("中文转英文热键格式无效：{Hotkey}", hotkeyText);
            return false;
        }

        if (IsTranslateRegistered && _translateGesture == gesture)
        {
            return true;
        }

        UnregisterTranslate();

        try
        {
            EnsureSource();

            IsTranslateRegistered = RegisterHotKey(
                _source!.Handle,
                TranslateHotkeyId,
                gesture.Modifiers | ModNoRepeat,
                gesture.VirtualKey);

            if (IsTranslateRegistered)
            {
                _translateGesture = gesture;
            }
            else
            {
                _logger.LogWarning(
                    "全局热键 {Hotkey} 注册失败，可能已被其他程序占用。",
                    gesture.DisplayText);
            }

            return IsTranslateRegistered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册中文转英文热键失败。");
            return false;
        }
    }

    public void Unregister()
    {
        if (_source is not null && IsRegistered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
        }

        IsRegistered = false;
        _captureGesture = null;
    }

    public void UnregisterTranslate()
    {
        if (_source is not null && IsTranslateRegistered)
        {
            UnregisterHotKey(_source.Handle, TranslateHotkeyId);
        }

        IsTranslateRegistered = false;
        _translateGesture = null;
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("VocabularyCoachHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HwndMessage,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotkeyId:
                handled = true;
                Pressed?.Invoke(this, EventArgs.Empty);
                break;

            case TranslateHotkeyId:
                handled = true;
                TranslatePressed?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        UnregisterTranslate();

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
