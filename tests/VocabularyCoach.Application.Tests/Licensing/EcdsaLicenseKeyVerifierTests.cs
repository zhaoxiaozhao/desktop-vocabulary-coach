using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.Application.Tests.Licensing;

public class EcdsaLicenseKeyVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_AcceptsSignedKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var licenseKey = CreateLicenseKey(key, new
        {
            v = 1,
            id = "XHS-0001",
            edition = "lifetime",
            iat = Now,
        });

        var verifier = new EcdsaLicenseKeyVerifier(
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

        var ok = verifier.TryVerify(licenseKey, out var payload, out var error);
        Assert.True(ok, error);
        Assert.Equal(string.Empty, error);
        Assert.NotNull(payload);
        Assert.Equal("XHS-0001", payload!.Id);
        Assert.Equal("lifetime", payload.Edition);
    }

    [Fact]
    public void Verify_RejectsTamperedPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var licenseKey = CreateLicenseKey(key, new
        {
            v = 1,
            id = "XHS-0001",
            edition = "lifetime",
            iat = Now,
        });

        var parts = licenseKey.Split('.');
        var payload = FromBase64Url(parts[1]);
        payload[10] ^= 0x01;
        var tampered = $"DVC1.{ToBase64Url(payload)}.{parts[2]}";

        var verifier = new EcdsaLicenseKeyVerifier(
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

        Assert.False(verifier.TryVerify(tampered, out _, out var error));
        Assert.Contains("无效", error);
    }

    [Fact]
    public void Verify_RejectsMalformedKey()
    {
        var verifier = new EcdsaLicenseKeyVerifier(
            Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256)
                .ExportSubjectPublicKeyInfo()));

        Assert.False(verifier.TryVerify("not-a-license", out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    internal static string CreateLicenseKey(ECDsa key, object payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = key.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"DVC1.{ToBase64Url(payloadBytes)}.{ToBase64Url(signature)}";
    }

    internal static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromBase64Url(string value)
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
