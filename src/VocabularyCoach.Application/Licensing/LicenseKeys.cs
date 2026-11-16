namespace VocabularyCoach.Application.Licensing;

/// <summary>
/// 内置公钥。私钥只保存在开发者本机（%USERPROFILE%\.dvc\signing-key.pem），
/// 绝不提交到仓库；更换密钥需要发布新版本。
/// </summary>
public static class LicenseKeys
{
    /// <summary>ECDSA P-256 公钥（SubjectPublicKeyInfo, Base64）。</summary>
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgQ3TTIedvyBQCEZuu7L4+g47fdtJT+qB2Nl5W0UbWbuTFJAk3S/1fdsaQXaY8srlli2OZ9+0HL6K8HDl/E6RkQ==";

    /// <summary>激活码前缀。</summary>
    public const string KeyPrefix = "DVC1";
}
