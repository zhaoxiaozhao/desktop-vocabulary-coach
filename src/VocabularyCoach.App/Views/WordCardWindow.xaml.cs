using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.App.ViewModels;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.App.Views;

/// <summary>
/// 桌面浮动单词卡片。无边框、置顶、不抢键盘焦点，可拖动。
/// </summary>
public partial class WordCardWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private readonly WordCardViewModel _viewModel;
    private readonly CardDisplayOptions _options;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly TaskCompletionSource<WordCardResult> _resultSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WordCardResult? _pendingResult;
    private bool _isClosing;
    private bool _isClosed;
    private bool _isDragging;
    private Point _dragStartScreenPixels;
    private Point _dragStartWindowDip;

    public WordCardWindow(WordCardViewModel viewModel, CardDisplayOptions options)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _options = options;

        DataContext = viewModel;
        Width = options.Width;
        _viewModel.ResultSubmitted += OnResultSubmitted;

        if (options.Animate)
        {
            Opacity = 0;
        }

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, options.AutoCloseSeconds)),
        };
        _autoCloseTimer.Tick += OnAutoCloseTick;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
    }

    /// <summary>卡片最终结果；窗口关闭后完成。</summary>
    public Task<WordCardResult> Result => _resultSource.Task;

    /// <summary>外部请求关闭卡片（应用退出、调度取消）。</summary>
    public void RequestClose(WordCardResult result)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RequestClose(result));
            return;
        }

        _ = CloseWithResultAsync(result);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WS_EX_NOACTIVATE：点击卡片不激活窗口，不打断用户当前输入。
        // WS_EX_TOOLWINDOW：不出现在 Alt+Tab 与应用切换列表中。
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, extendedStyle | WsExNoActivate | WsExToolWindow);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottomRight();

        if (_options.Animate)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        if (_options.AutoCloseSeconds > 0)
        {
            _autoCloseTimer.Start();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        // 展开释义后高度增加时，避免卡片超出当前屏幕工作区。
        var (workArea, _) = GetTargetWorkArea();
        var maxTop = workArea.Bottom - _options.EdgeMargin - ActualHeight;
        if (Top > maxTop)
        {
            Top = Math.Max(workArea.Top + _options.EdgeMargin, maxTop);
        }
    }

    private void PositionAtBottomRight()
    {
        var (workArea, _) = GetTargetWorkArea();
        Left = Math.Max(workArea.Left + _options.EdgeMargin, workArea.Right - ActualWidth - _options.EdgeMargin);
        Top = Math.Max(workArea.Top + _options.EdgeMargin, workArea.Bottom - ActualHeight - _options.EdgeMargin);
    }

    /// <summary>取鼠标所在显示器的工作区（DIP）；多显示器与混合 DPI 下按该屏幕缩放换算。</summary>
    private (Rect WorkArea, double Scale) GetTargetWorkArea()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var bounds = screen.WorkingArea;

        var monitor = MonitorFromPoint(
            new NativePoint { X = bounds.Left + 1, Y = bounds.Top + 1 },
            MonitorDefaultToNearest);

        double scaleX;
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 &&
            dpiX > 0)
        {
            scaleX = dpiX / 96.0;
        }
        else
        {
            scaleX = VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        var workArea = new Rect(
            bounds.Left / scaleX,
            bounds.Top / scaleX,
            bounds.Width / scaleX,
            bounds.Height / scaleX);

        return (workArea, scaleX);
    }

    private void OnAutoCloseTick(object? sender, EventArgs e)
    {
        _ = CloseWithResultAsync(WordCardResult.Timeout);
    }

    private void OnResultSubmitted(object? sender, WordCardResult result)
    {
        _ = CloseWithResultAsync(result);
    }

    private async Task CloseWithResultAsync(WordCardResult result)
    {
        if (_isClosing || _isClosed)
        {
            return;
        }

        _isClosing = true;
        _pendingResult = result;
        _autoCloseTimer.Stop();

        if (_options.Animate && IsVisible)
        {
            var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            BeginAnimation(OpacityProperty, fadeOut);
            await Task.Delay(130);
        }

        if (!_isClosed)
        {
            Close();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _autoCloseTimer.Stop();
        _autoCloseTimer.Tick -= OnAutoCloseTick;
        _viewModel.ResultSubmitted -= OnResultSubmitted;
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;

        // 关闭按钮、取消、异常路径都视为“未提供反馈”。
        _resultSource.TrySetResult(_pendingResult ?? WordCardResult.Dismissed);
    }

    private void OnDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _isDragging = true;
        _dragStartScreenPixels = PointToScreen(e.GetPosition(this));
        _dragStartWindowDip = new Point(Left, Top);
        CardRoot.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // PointToScreen 返回物理像素，换算为 DIP 后移动窗口，兼容高 DPI。
        var currentScreenPixels = PointToScreen(Mouse.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        var deltaX = (currentScreenPixels.X - _dragStartScreenPixels.X) / dpi.DpiScaleX;
        var deltaY = (currentScreenPixels.Y - _dragStartScreenPixels.Y) / dpi.DpiScaleY;

        Left = _dragStartWindowDip.X + deltaX;
        Top = _dragStartWindowDip.Y + deltaY;
    }

    private void OnDragHandleMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        CardRoot.ReleaseMouseCapture();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
