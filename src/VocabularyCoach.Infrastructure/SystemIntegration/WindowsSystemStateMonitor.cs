using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.Infrastructure.SystemIntegration;

/// <summary>
/// 使用 Windows API 检测空闲、锁屏/会话断开与前台全屏应用。
/// 所有 P/Invoke 集中在这里，业务层只看到 <see cref="SystemState"/>。
/// </summary>
public sealed class WindowsSystemStateMonitor : ISystemStateMonitor
{
    private const uint DesktopSwitchDesktop = 0x0100;
    private const uint MonitorDefaultToNearest = 2;
    private const int WtsCurrentSession = -1;
    private const int WtsConnectState = 8;
    private const int WtsActive = 0;
    private const int FullscreenTolerancePixels = 2;

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    private readonly ISettingsService _settings;
    private readonly ILogger<WindowsSystemStateMonitor> _logger;
    private bool _wtsFailureLogged;

    public WindowsSystemStateMonitor(
        ISettingsService settings,
        ILogger<WindowsSystemStateMonitor> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task<SystemState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var options = _settings.Scheduler;
            var idleTime = GetUserIdleTime();
            var isIdle = options.EnableIdleDetection &&
                         idleTime >= TimeSpan.FromMinutes(options.IdleThresholdMinutes);
            var isLocked = IsSessionLocked();
            var isFullscreen = options.DetectFullscreen && IsForegroundWindowFullscreen();

            return Task.FromResult(new SystemState
            {
                IdleTime = idleTime,
                IsIdle = isIdle,
                IsSessionLocked = isLocked,
                IsFullscreenAppActive = isFullscreen,
            });
        }
        catch (Exception ex)
        {
            // 检测失败时保守处理：暂缓弹出并记录日志。
            _logger.LogWarning(ex, "系统状态检测失败，本次暂缓弹出。");
            return Task.FromResult(new SystemState { DetectionFailed = true });
        }
    }

    private static TimeSpan GetUserIdleTime()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetLastInputInfo 失败。");
        }

        var idleMilliseconds = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    private bool IsSessionLocked()
    {
        if (IsSessionDisconnected())
        {
            return true;
        }

        // 锁屏或安全桌面（UAC）活动时 OpenInputDesktop 会失败。
        var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero)
        {
            return true;
        }

        CloseDesktop(desktop);
        return false;
    }

    private bool IsSessionDisconnected()
    {
        try
        {
            if (!WTSQuerySessionInformation(
                    IntPtr.Zero,
                    WtsCurrentSession,
                    WtsConnectState,
                    out var buffer,
                    out _) ||
                buffer == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return Marshal.ReadInt32(buffer) != WtsActive;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        catch (DllNotFoundException)
        {
            if (!_wtsFailureLogged)
            {
                _wtsFailureLogged = true;
                _logger.LogDebug("wtsapi32 不可用，仅使用桌面句柄判断锁屏。");
            }

            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool IsForegroundWindowFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            // 自己的卡片/窗口不算全屏应用。
            return false;
        }

        if (!IsWindowVisible(foreground) || IsIconic(foreground))
        {
            return false;
        }

        var className = GetClassName(foreground);
        if (ShellWindowClasses.Contains(className))
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var screen = monitorInfo.Monitor;
        return windowRect.Left <= screen.Left + FullscreenTolerancePixels &&
               windowRect.Top <= screen.Top + FullscreenTolerancePixels &&
               windowRect.Right >= screen.Right - FullscreenTolerancePixels &&
               windowRect.Bottom >= screen.Bottom - FullscreenTolerancePixels;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        int wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);
}
