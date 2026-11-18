using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Ai;

namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>
/// 每个 AI 连接一个 DPAPI 加密文件：ai.&lt;connection-id&gt;.key。
/// 旧版单连接 ai.key 在读取 default 连接时自动迁移。
/// </summary>
public sealed class DpapiAiKeyStore : IAiKeyStore
{
    private readonly string _directory;
    private readonly string _legacyKeyPath;
    private readonly ILogger<DpapiAiKeyStore> _logger;

    public DpapiAiKeyStore(
        string directory,
        string legacyKeyPath,
        ILogger<DpapiAiKeyStore> logger)
    {
        _directory = directory;
        _legacyKeyPath = legacyKeyPath;
        _logger = logger;
        Directory.CreateDirectory(directory);
    }

    public async Task<string?> GetAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(connectionId);

        if (File.Exists(path))
        {
            return ReadProtected(path);
        }

        // 旧版 ai.key 迁移到 default 连接。
        if (string.Equals(connectionId, "default", StringComparison.OrdinalIgnoreCase)
            && File.Exists(_legacyKeyPath))
        {
            var legacy = ReadProtected(_legacyKeyPath);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                await SetAsync(connectionId, legacy, cancellationToken).ConfigureAwait(false);
                return legacy;
            }
        }

        return null;
    }

    public Task SetAsync(
        string connectionId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return DeleteAsync(connectionId, cancellationToken);
        }

        var path = GetPath(connectionId);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey.Trim()),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, path, overwrite: true);

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(connectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string? ReadProtected(string path)
    {
        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "无法解密 AI 连接 Key：{Path}", path);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取 AI 连接 Key 失败：{Path}", path);
            return null;
        }
    }

    private string GetPath(string connectionId)
    {
        var safeId = new string(
            connectionId
                .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
                .ToArray());

        if (safeId.Length == 0)
        {
            safeId = "default";
        }

        return Path.Combine(_directory, $"ai.{safeId}.key");
    }
}
