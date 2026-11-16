using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.Infrastructure.Licensing;

/// <summary>
/// 使用 Windows DPAPI（当前用户）加密保存激活码。
/// 文件位于 %LocalAppData%\DesktopVocabularyCoach\license.dat，不可跨用户复制。
/// </summary>
public sealed class DpapiLicenseStore : ILicenseStore
{
    private readonly string _path;
    private readonly ILogger<DpapiLicenseStore> _logger;

    public DpapiLicenseStore(string path, ILogger<DpapiLicenseStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            return Task.FromResult<string?>(Encoding.UTF8.GetString(plainBytes));
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "无法解密许可证文件，按未激活处理。");
            return Task.FromResult<string?>(null);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取许可证文件失败。");
            return Task.FromResult<string?>(null);
        }
    }

    public Task WriteAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(licenseKey),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var temporaryPath = _path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _path, overwrite: true);

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return Task.CompletedTask;
    }
}
