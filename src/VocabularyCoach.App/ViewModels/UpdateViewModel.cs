using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Updates;

namespace VocabularyCoach.App.ViewModels;

/// <summary>检查更新、下载并启动安装程序。</summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    public const string ReleasesUrl =
        "https://github.com/zhaoxiaozhao/desktop-vocabulary-coach/releases/latest";

    private readonly IUpdateService _updateService;
    private readonly IAppVersion _appVersion;
    private readonly ILogger<UpdateViewModel> _logger;

    private UpdateManifest? _manifest;

    public UpdateViewModel(
        IUpdateService updateService,
        IAppVersion appVersion,
        ILogger<UpdateViewModel> logger)
    {
        _updateService = updateService;
        _appVersion = appVersion;
        _logger = logger;
        CurrentVersionText = $"当前版本 {appVersion.Current.ToString(3)}";
    }

    public string CurrentVersionText { get; }

    [ObservableProperty]
    private string _statusText = "正在检查更新…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string _notesText = string.Empty;

    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _canInstall;

    public async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CanInstall = false;
        Progress = 0;
        StatusText = "正在检查更新…";
        NotesText = string.Empty;
        Message = string.Empty;
        _manifest = null;

        try
        {
            var result = await _updateService.CheckAsync();

            if (result.UpdateAvailable && result.Manifest is not null)
            {
                StatusText = $"发现新版本 {result.Manifest.Version}";
                NotesText = result.Manifest.Notes.Count > 0
                    ? string.Join(Environment.NewLine, result.Manifest.Notes.Select(n => "• " + n))
                    : "该版本没有提供更新说明。";
                _manifest = result.Manifest;
                CanInstall = true;
                Message = string.Empty;
            }
            else
            {
                StatusText = result.Manifest is null ? "检查更新失败" : "当前已是最新版本";
                NotesText = result.Manifest is null
                    ? string.Empty
                    : "你正在使用最新版本。" + Environment.NewLine
                      + "应用会在后台每 24 小时自动检查一次更新，也可以在托盘菜单中手动检查。";
                _manifest = null;
                CanInstall = false;
                Message = result.Manifest is null ? result.Message : string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查更新失败。");
            StatusText = "检查更新失败";
            Message = "无法连接更新服务器，请稍后重试。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckAgainAsync() => await CheckAsync();

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (_manifest is null)
        {
            return;
        }

        IsBusy = true;
        CanInstall = false;
        Progress = 0;
        Message = "正在下载安装包…";

        try
        {
            var progress = new Progress<double>(value => Progress = Math.Round(value * 100, 1));
            var path = await _updateService.DownloadAsync(_manifest, progress);

            Message = "下载完成，正在启动安装程序…";
            _updateService.LaunchInstaller(path);

            // 安装程序会等待本进程退出后替换文件；随后由安装包重新启动应用。
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载或安装更新失败。");
            Message = "更新失败：" + ex.Message;
            IsBusy = false;
            CanInstall = true;
        }
    }

    [RelayCommand]
    private void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开 Releases 页面失败。");
            Message = "无法打开浏览器，请手动访问：" + ReleasesUrl;
        }
    }
}
