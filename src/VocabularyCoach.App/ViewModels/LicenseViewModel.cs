using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.App.ViewModels;

/// <summary>许可证窗口：查看试用状态、粘贴激活码、打开购买页面。</summary>
public sealed partial class LicenseViewModel : ObservableObject
{
    public const string PurchaseUrl =
        "https://desktop-vocabulary-coach.zhaojia5201314.workers.dev/#install";

    private readonly ILicenseService _license;
    private readonly ILogger<LicenseViewModel> _logger;

    public LicenseViewModel(ILicenseService license, ILogger<LicenseViewModel> logger)
    {
        _license = license;
        _logger = logger;
        Refresh();
    }

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _detailText = string.Empty;

    [ObservableProperty]
    private string _licenseKeyInput = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public void Refresh()
    {
        var snapshot = _license.Current;
        switch (snapshot.Status)
        {
            case LicenseStatus.Licensed:
                StatusText = "已激活";
                DetailText = snapshot.Edition == "lifetime"
                    ? $"终身版 · 许可证 {snapshot.LicenseId}"
                    : $"{snapshot.Edition} · 许可证 {snapshot.LicenseId}";
                break;

            case LicenseStatus.Trial:
                StatusText = $"免费试用中 · 剩余 {snapshot.DaysRemaining} 天";
                DetailText = snapshot.TrialEndsAt is { } ends
                    ? $"试用将于 {ends.LocalDateTime:yyyy-MM-dd HH:mm} 结束；到期后自动弹卡与生词捕获会暂停，学习数据不受影响。"
                    : "试用期内可使用全部功能。";
                break;

            default:
                StatusText = "试用已结束";
                DetailText = "激活后可继续自动弹卡与生词捕获；已保存的单词、进度和复习记录不会丢失。";
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task ActivateAsync()
    {
        IsBusy = true;
        Message = "正在验证激活码…";

        try
        {
            var result = await _license.ActivateAsync(LicenseKeyInput);
            Message = result.Message;

            if (result.Success)
            {
                LicenseKeyInput = string.Empty;
                Refresh();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "激活许可证失败。");
            Message = "激活失败，请检查激活码后重试。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanActivate() => !IsBusy && !string.IsNullOrWhiteSpace(LicenseKeyInput);

    partial void OnLicenseKeyInputChanged(string value) => ActivateCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => ActivateCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void OpenPurchasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PurchaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开购买页面失败。");
            Message = "无法打开浏览器，请手动访问：" + PurchaseUrl;
        }
    }
}
