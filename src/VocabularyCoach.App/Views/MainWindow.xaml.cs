using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

/// <summary>
/// 主窗口。关闭时默认隐藏到托盘，只有 App 主动退出时才真正关闭。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += OnStatusTimerTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>窗口被隐藏到托盘时触发，用于提示用户。</summary>
    public event EventHandler? HiddenToTray;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        (DataContext as MainViewModel)?.RefreshState();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is not App { IsShuttingDown: true })
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _statusTimer.Start();

    private void OnClosed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
    }

    private void OnStatusTimerTick(object? sender, EventArgs e) =>
        (DataContext as MainViewModel)?.RefreshState();
}
