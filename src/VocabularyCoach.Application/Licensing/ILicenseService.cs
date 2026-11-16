namespace VocabularyCoach.Application.Licensing;

/// <summary>读取、激活与查询许可证状态。</summary>
public interface ILicenseService
{
    /// <summary>当前状态（试用中 / 试用已结束 / 已激活）。</summary>
    LicenseSnapshot Current { get; }

    /// <summary>是否允许弹卡、生词捕获等付费功能。</summary>
    bool CanUsePremiumFeatures { get; }

    /// <summary>状态变化（激活、试用到期检测到变化）。</summary>
    event EventHandler? Changed;

    /// <summary>启动时加载已保存的许可证与试用状态。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>验证并保存激活码。</summary>
    Task<LicenseActivationResult> ActivateAsync(
        string licenseKey,
        CancellationToken cancellationToken = default);

    /// <summary>重新计算试用剩余时间并保存最后活跃时间。</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
