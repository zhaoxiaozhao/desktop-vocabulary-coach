namespace VocabularyCoach.Application.Updates;

/// <summary>检查、下载并启动更新安装程序。</summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>下载安装包并校验 SHA256，返回本地路径。</summary>
    Task<string> DownloadAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>以静默方式启动安装程序；调用方随后应退出应用。</summary>
    void LaunchInstaller(string installerPath);
}
