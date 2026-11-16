using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Ai;

namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>
/// Windows DPAPI（当前用户）加密的 API Key 存储。文件位于本地应用数据目录，
/// 内容不可移植，也不会出现在配置、数据库或日志中。
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;
    private readonly ILogger<DpapiSecretStore> _logger;

    public DpapiSecretStore(string path, ILogger<DpapiSecretStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public Task SaveAsync(string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var temporaryPath = _path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _path, overwrite: true);

        return Task.CompletedTask;
    }

    public Task<string?> TryReadAsync(CancellationToken cancellationToken)
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
            _logger.LogWarning(ex, "无法解密已保存的 AI API Key，请重新设置。");
            return Task.FromResult<string?>(null);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取 AI API Key 失败。");
            return Task.FromResult<string?>(null);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return Task.CompletedTask;
    }
}
