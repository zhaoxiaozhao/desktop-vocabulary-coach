namespace VocabularyCoach.Application.Licensing;

/// <summary>验证离线激活码的 ECDSA 签名。</summary>
public interface ILicenseKeyVerifier
{
    bool TryVerify(string licenseKey, out LicensePayload? payload, out string error);
}
