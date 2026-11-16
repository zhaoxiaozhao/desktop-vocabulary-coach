using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VocabularyCoach.Application.Updates;
using VocabularyCoach.Infrastructure.Diagnostics;

namespace VocabularyCoach.Infrastructure.Updates;

/// <summary>
/// 从 OSS / GitHub 获取已签名更新清单，下载安装包并校验 SHA256。
/// 清单签名使用与许可证相同的离线密钥，公钥内置在应用中。
/// </summary>
public sealed class HttpUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _options;
    private readonly IAppVersion _appVersion;
    private readonly ILogger<HttpUpdateService> _logger;

    public HttpUpdateService(
        HttpClient httpClient,
        IOptions<UpdateOptions> options,
        IAppVersion appVersion,
        ILogger<HttpUpdateService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _appVersion = appVersion;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = _appVersion.Current;
        var currentText = current.ToString(3);

        if (_options.ManifestUrls.Length == 0)
        {
            return new UpdateCheckResult(false, currentText, null, "未配置更新地址。");
        }

        var errors = new List<string>();

        foreach (var url in _options.ManifestUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!UpdateManifestVerifier.TryVerify(json, out var manifest, out var error) || manifest is null)
                {
                    _logger.LogWarning("更新清单无效（{Url}）：{Error}", url, error);
                    errors.Add(error);
                    continue;
                }

                if (!Version.TryParse(manifest.Version, out var remote))
                {
                    _logger.LogWarning("更新清单版本号无效：{Version}", manifest.Version);
                    continue;
                }

                if (remote.CompareTo(current) <= 0)
                {
                    return new UpdateCheckResult(false, currentText, manifest, "当前已是最新版本。");
                }

                _logger.LogInformation(
                    "发现新版本：{Remote}（当前 {Current}）。",
                    manifest.Version,
                    currentText);

                return new UpdateCheckResult(
                    true,
                    currentText,
                    manifest,
                    $"发现新版本 {manifest.Version}。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "检查更新失败：{Url}", url);
                errors.Add(ex.Message);
            }
        }

        return new UpdateCheckResult(
            false,
            currentText,
            null,
            errors.Count > 0 ? "检查更新失败：" + errors[0] : "检查更新失败，请稍后重试。");
    }

    public async Task<string> DownloadAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = AppPaths.EnsureDirectory(Path.Combine(AppPaths.RootDirectory, "updates"));
        var fileName = $"DesktopVocabularyCoach-Setup-{manifest.Version}.exe";
        var targetPath = Path.Combine(directory, fileName);

        var urls = new List<string> { manifest.InstallerUrl };
        if (!string.IsNullOrWhiteSpace(manifest.FallbackUrl))
        {
            urls.Add(manifest.FallbackUrl);
        }

        Exception? lastError = null;

        // 下载 50+ MB 文件时不能沿用 AI 请求的 30 秒超时。
        using var downloadClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        };

        foreach (var url in urls)
        {
            try
            {
                await DownloadFromAsync(
                        downloadClient,
                        url,
                        targetPath,
                        manifest,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);

                return targetPath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "下载更新失败：{Url}", url);
            }
        }

        throw new InvalidOperationException(
            "下载更新失败：" + (lastError?.Message ?? "没有可用的下载地址。"),
            lastError);
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("找不到已下载的安装包。", installerPath);
        }

        _logger.LogInformation("启动更新安装程序：{Path}", installerPath);

        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /UPDATE",
        });
    }

    private static async Task DownloadFromAsync(
        HttpClient client,
        string url,
        string targetPath,
        UpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? manifest.Size;
        var temporaryPath = targetPath + ".part";

        try
        {
            await using (var source = await response.Content
                             .ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            await using (var target = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;

                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1d, (double)received / total));
                    }
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);

                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"安装包校验失败（期望 {manifest.Sha256}，实际 {actualHash}）。");
                }

                if (received != manifest.Size)
                {
                    throw new InvalidDataException(
                        $"安装包大小不一致（期望 {manifest.Size} 字节，实际 {received} 字节）。");
                }
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            progress?.Report(1d);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // 忽略临时文件清理失败。
                }
            }
        }
    }
}
