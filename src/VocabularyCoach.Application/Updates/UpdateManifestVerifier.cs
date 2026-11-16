using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.Application.Updates;

/// <summary>
/// 验证更新清单的 ECDSA 签名。清单格式：
/// { "payload": "&lt;base64url JSON&gt;", "signature": "&lt;base64url&gt;" }。
/// 与许可证共用同一把离线签名密钥（公钥内置，私钥只在开发者本机）。
/// </summary>
public static class UpdateManifestVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryVerify(
        string envelopeJson,
        out UpdateManifest? manifest,
        out string error,
        string? publicKeyBase64 = null)
    {
        manifest = null;
        error = string.Empty;

        SignedEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SignedEnvelope>(envelopeJson, JsonOptions);
        }
        catch (JsonException)
        {
            error = "更新清单不是有效的 JSON。";
            return false;
        }

        if (envelope is null
            || string.IsNullOrWhiteSpace(envelope.Payload)
            || string.IsNullOrWhiteSpace(envelope.Signature))
        {
            error = "更新清单缺少 payload 或 signature。";
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = FromBase64Url(envelope.Payload);
            signature = FromBase64Url(envelope.Signature);
        }
        catch (FormatException)
        {
            error = "更新清单的 Base64 内容无效。";
            return false;
        }

        try
        {
            using var publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(publicKeyBase64 ?? LicenseKeys.PublicKeyBase64),
                out _);

            if (!publicKey.VerifyData(
                    payloadBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                error = "更新清单签名无效。";
                return false;
            }
        }
        catch (CryptographicException)
        {
            error = "更新清单签名校验失败。";
            return false;
        }

        UpdatePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdatePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            error = "更新清单内容无法解析。";
            return false;
        }

        if (payload is null || payload.V != 1)
        {
            error = "更新清单版本不受支持。";
            return false;
        }

        var versionText = payload.Version;
        if (string.IsNullOrWhiteSpace(versionText) || !Version.TryParse(versionText, out _))
        {
            error = "更新清单中的版本号无效。";
            return false;
        }

        var downloadUrl = payload.Url;
        if (downloadUrl is null
            || !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "更新清单中的下载地址必须是 HTTPS。";
            return false;
        }

        if (payload.Sha256 is not { Length: 64 }
            || !payload.Sha256.All(char.IsAsciiHexDigit))
        {
            error = "更新清单中的 SHA256 无效。";
            return false;
        }

        if (payload.Size <= 0)
        {
            error = "更新清单中的文件大小无效。";
            return false;
        }

        manifest = new UpdateManifest(
            versionText,
            payload.ReleasedAt ?? string.Empty,
            payload.Notes ?? [],
            downloadUrl,
            payload.FallbackUrl,
            payload.Sha256.ToLowerInvariant(),
            payload.Size,
            payload.Mandatory);

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

    private sealed record SignedEnvelope(string? Payload, string? Signature);

    private sealed record UpdatePayload(
        int V,
        string? Version,
        string? ReleasedAt,
        string[]? Notes,
        string? Url,
        string? FallbackUrl,
        string? Sha256,
        long Size,
        bool Mandatory);
}
