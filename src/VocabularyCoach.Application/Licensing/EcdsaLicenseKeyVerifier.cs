using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VocabularyCoach.Application.Licensing;

/// <summary>
/// 离线激活码验证：DVC1.&lt;payload&gt;.&lt;signature&gt;。
/// 只验签，不联网、不绑定机器。
/// </summary>
public sealed class EcdsaLicenseKeyVerifier : ILicenseKeyVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ECDsa _publicKey;

    public EcdsaLicenseKeyVerifier(string? publicKeyBase64 = null)
    {
        var keyBytes = Convert.FromBase64String(publicKeyBase64 ?? LicenseKeys.PublicKeyBase64);
        _publicKey = ECDsa.Create();
        _publicKey.ImportSubjectPublicKeyInfo(keyBytes, out _);
    }

    public bool TryVerify(string licenseKey, out LicensePayload? payload, out string error)
    {
        payload = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            error = "激活码为空。";
            return false;
        }

        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], LicenseKeys.KeyPrefix, StringComparison.Ordinal))
        {
            error = "激活码格式不正确，应为 DVC1.…。";
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = FromBase64Url(parts[1]);
            signature = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            error = "激活码内容不是有效的 Base64 数据。";
            return false;
        }

        bool valid;
        try
        {
            valid = _publicKey.VerifyData(
                payloadBytes,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            error = "激活码签名校验失败。";
            return false;
        }

        if (!valid)
        {
            error = "激活码无效或已被修改。";
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            error = "激活码内容无法解析。";
            return false;
        }

        if (payload is null || payload.Version != 1 || string.IsNullOrWhiteSpace(payload.Id))
        {
            error = "激活码缺少必要信息。";
            payload = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.Edition))
        {
            error = "激活码未指定版本类型。";
            payload = null;
            return false;
        }

        return true;
    }

    private static byte[] FromBase64Url(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        text += (text.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        return Convert.FromBase64String(text);
    }
}
