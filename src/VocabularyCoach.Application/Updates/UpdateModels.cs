namespace VocabularyCoach.Application.Updates;

/// <summary>已签名的更新清单（payload 解码后）。</summary>
public sealed record UpdateManifest(
    string Version,
    string ReleasedAt,
    IReadOnlyList<string> Notes,
    string InstallerUrl,
    string? FallbackUrl,
    string Sha256,
    long Size,
    bool Mandatory);

/// <summary>检查更新结果。</summary>
public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    UpdateManifest? Manifest,
    string Message);

/// <summary>更新检查配置。</summary>
public sealed class UpdateOptions
{
    public const string SectionName = "Update";

    /// <summary>按顺序尝试的清单地址（阿里云 OSS 优先，GitHub 兜底）。</summary>
    public string[] ManifestUrls { get; set; } = [];

    /// <summary>启动后多少秒检查一次。</summary>
    public int CheckDelaySeconds { get; set; } = 30;

    /// <summary>后台检查间隔（小时）。</summary>
    public int CheckIntervalHours { get; set; } = 24;
}

/// <summary>当前应用版本。</summary>
public interface IAppVersion
{
    Version Current { get; }
}

public sealed class AppVersion : IAppVersion
{
    public Version Current =>
        typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 9, 0);
}
