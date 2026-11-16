using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VocabularyCoach.Application.Updates;

namespace VocabularyCoach.Application.Tests.Updates;

public class UpdateManifestVerifierTests
{
    [Fact]
    public void Verify_AcceptsSignedManifest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = CreateEnvelope(key, CreatePayload());

        var ok = UpdateManifestVerifier.TryVerify(
            envelope,
            out var manifest,
            out var error,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        Assert.True(ok, error);
        Assert.Equal(string.Empty, error);
        Assert.NotNull(manifest);
        Assert.Equal("9.9.9", manifest!.Version);
        Assert.Equal("https://example.com/app.exe", manifest.InstallerUrl);
        Assert.Equal(123456L, manifest.Size);
        Assert.False(manifest.Mandatory);
    }

    [Fact]
    public void Verify_RejectsTamperedManifest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envelope = CreateEnvelope(key, CreatePayload());

        using var document = JsonDocument.Parse(envelope);
        var payloadText = document.RootElement.GetProperty("payload").GetString()!;
        var signature = document.RootElement.GetProperty("signature").GetString()!;
        var payloadBytes = FromBase64Url(payloadText);
        payloadBytes[10] ^= 0x01;

        var tampered = JsonSerializer.Serialize(new
        {
            payload = ToBase64Url(payloadBytes),
            signature,
        });

        Assert.False(UpdateManifestVerifier.TryVerify(
            tampered,
            out _,
            out var error,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        Assert.Contains("签名", error);
    }

    [Fact]
    public void Verify_RejectsNonHttpsUrl()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = CreatePayload();
        var envelope = CreateEnvelope(key, new
        {
            payload.V,
            payload.Version,
            payload.ReleasedAt,
            payload.Notes,
            Url = "http://example.com/app.exe",
            payload.FallbackUrl,
            payload.Sha256,
            payload.Size,
            payload.Mandatory,
        });

        Assert.False(UpdateManifestVerifier.TryVerify(
            envelope,
            out _,
            out var error,
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        Assert.Contains("HTTPS", error);
    }

    private static Payload CreatePayload() => new(
        V: 1,
        Version: "9.9.9",
        ReleasedAt: "2026-11-16",
        Notes: ["修复问题", "提升性能"],
        Url: "https://example.com/app.exe",
        FallbackUrl: null,
        Sha256: new string('a', 64),
        Size: 123456,
        Mandatory: false);

    private static string CreateEnvelope(ECDsa key, object payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = key.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return JsonSerializer.Serialize(new
        {
            payload = ToBase64Url(payloadBytes),
            signature = ToBase64Url(signature),
        });
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    private sealed record Payload(
        int V,
        string Version,
        string ReleasedAt,
        string[] Notes,
        string Url,
        string? FallbackUrl,
        string Sha256,
        long Size,
        bool Mandatory);
}
