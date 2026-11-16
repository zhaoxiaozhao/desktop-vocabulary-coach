using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace VocabularyCoach.App.Services;

/// <summary>全局热键：默认 Ctrl+Alt+W，用于捕获当前选中的单词/短语。</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5643;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkW = 0x57;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly ILogger<GlobalHotkeyService> _logger;
    private HwndSource? _source;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? Pressed;

    public bool IsRegistered { get; private set; }

    public bool TryRegister()
    {
        if (IsRegistered)
        {
            return true;
        }

        try
        {
            if (_source is null)
            {
                var parameters = new HwndSourceParameters("VocabularyCoachHotkeyWindow")
                {
                    Width = 0,
                    Height = 0,
                    ParentWindow = HwndMessage,
                };

                _source = new HwndSource(parameters);
                _source.AddHook(WndProc);
            }

            IsRegistered = RegisterHotKey(
                _source.Handle,
                HotkeyId,
                ModControl | ModAlt | ModNoRepeat,
                VkW);

            if (!IsRegistered)
            {
                _logger.LogWarning("全局热键 Ctrl+Alt+W 注册失败，可能已被其他程序占用。");
            }

            return IsRegistered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册全局热键失败。");
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
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();

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
