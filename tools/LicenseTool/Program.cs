using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VocabularyCoach.LicenseTool;

/// <summary>
/// 离线许可证与更新清单签名工具。
/// 私钥默认保存在 %USERPROFILE%\.dvc\signing-key.pem，绝不提交到仓库。
/// </summary>
public static class Program
{
    private const string KeyPrefix = "DVC1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            var keyPath = options.GetValueOrDefault("key-file")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".dvc",
                    "signing-key.pem");

            return command switch
            {
                "init" => Init(keyPath, options.ContainsKey("force")),
                "public-key" => PrintPublicKey(keyPath),
                "license" => CreateLicense(keyPath, options),
                "manifest" => CreateManifest(keyPath, options),
                "verify-license" => VerifyLicense(keyPath, options),
                "verify-manifest" => VerifyManifest(keyPath, options),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("错误：" + ex.Message);
            return 2;
        }
    }

    private static int Init(string keyPath, bool force)
    {
        if (File.Exists(keyPath) && !force)
        {
            Console.WriteLine("私钥已存在，未覆盖：" + keyPath);
            PrintPublicKey(keyPath);
            return 0;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(keyPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));

        Console.WriteLine("已生成 ECDSA P-256 签名密钥。");
        Console.WriteLine("私钥：" + keyPath);
        Console.WriteLine("请立即备份该文件；丢失后无法再签发许可证或更新清单。");
        Console.WriteLine();
        Console.WriteLine("PublicKeyBase64:");
        Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    private static int PrintPublicKey(string keyPath)
    {
        using var key = LoadKey(keyPath);
        Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        return 0;
    }

    private static int CreateLicense(string keyPath, Dictionary<string, string> options)
    {
        var id = Require(options, "id");
        var edition = options.GetValueOrDefault("edition") ?? "lifetime";
        var note = options.GetValueOrDefault("note");

        var payload = new LicensePayload(
            V: 1,
            Id: id,
            Edition: edition,
            Iat: DateTimeOffset.UtcNow,
            Note: note);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var key = LoadKey(keyPath);
        var signature = key.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Console.WriteLine($"{KeyPrefix}.{ToBase64Url(payloadBytes)}.{ToBase64Url(signature)}");
        return 0;
    }

    private static int CreateManifest(string keyPath, Dictionary<string, string> options)
    {
        var version = Require(options, "version");
        var url = Require(options, "url");
        var sha256 = Require(options, "sha256");
        var size = long.Parse(Require(options, "size"));
        var fallback = options.GetValueOrDefault("fallback");
        var notes = options.TryGetValue("notes", out var joined)
            ? joined.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        var payload = new UpdatePayload(
            V: 1,
            Version: version,
            ReleasedAt: options.GetValueOrDefault("released") ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            Notes: notes,
            Url: url,
            FallbackUrl: fallback,
            Sha256: sha256.ToLowerInvariant(),
            Size: size,
            Mandatory: options.ContainsKey("mandatory"));

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var key = LoadKey(keyPath);
        var signature = key.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var envelope = new SignedEnvelope(
            ToBase64Url(payloadBytes),
            ToBase64Url(signature));

        var output = options.GetValueOrDefault("output") ?? "updates/latest.json";
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            output,
            JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }),
            new UTF8Encoding(false));

        Console.WriteLine("已写入 " + Path.GetFullPath(output));
        return 0;
    }

    private static int VerifyLicense(string keyPath, Dictionary<string, string> options)
    {
        var value = Require(options, "license");
        var parts = value.Split('.');
        if (parts.Length != 3 || parts[0] != KeyPrefix)
        {
            Console.WriteLine("格式无效。");
            return 3;
        }

        var payloadBytes = FromBase64Url(parts[1]);
        var signature = FromBase64Url(parts[2]);
        using var key = LoadKey(keyPath);
        var valid = key.VerifyData(
            payloadBytes,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Console.WriteLine(valid ? "签名有效" : "签名无效");
        if (valid)
        {
            Console.WriteLine(Encoding.UTF8.GetString(payloadBytes));
        }

        return valid ? 0 : 3;
    }

    private static int VerifyManifest(string keyPath, Dictionary<string, string> options)
    {
        var path = options.GetValueOrDefault("file") ?? "updates/latest.json";
        var envelope = JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("无法解析清单文件。");

        var payloadBytes = FromBase64Url(envelope.Payload);
        var signature = FromBase64Url(envelope.Signature);
        using var key = LoadKey(keyPath);
        var valid = key.VerifyData(
            payloadBytes,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Console.WriteLine(valid ? "签名有效" : "签名无效");
        if (valid)
        {
            Console.WriteLine(Encoding.UTF8.GetString(payloadBytes));
        }

        return valid ? 0 : 3;
    }

    private static ECDsa LoadKey(string keyPath)
    {
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException("找不到私钥，请先运行 init。", keyPath);
        }

        var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyPath));
        return key;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[name] = args[++i];
            }
            else
            {
                result[name] = "true";
            }
        }

        return result;
    }

    private static string Require(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new InvalidOperationException("缺少参数 --" + name);

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

    private static int Unknown(string command)
    {
        Console.Error.WriteLine("未知命令：" + command);
        PrintUsage();
        return 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            Desktop Vocabulary Coach 签名工具

            init                 生成签名密钥（默认 %USERPROFILE%\.dvc\signing-key.pem）
            public-key           输出公钥（Base64 SPKI）
            license              生成激活码
              --id XHS-0001 [--note "..."] [--edition lifetime]
            manifest             生成并签名更新清单
              --version 0.9.0 --url https://... --sha256 <hash> --size <bytes>
              [--fallback https://...] [--notes "更新说明1|更新说明2"]
              [--mandatory] [--output updates/latest.json]
            verify-license       验证激活码  --license DVC1...
            verify-manifest      验证清单    [--file updates/latest.json]
            """);
        return 0;
    }

    private sealed record LicensePayload(int V, string Id, string Edition, DateTimeOffset Iat, string? Note);

    private sealed record UpdatePayload(
        int V,
        string Version,
        string ReleasedAt,
        string[] Notes,
        string Url,
        string? FallbackUrl,
        string Sha256,
        long Size,
        bool Mandatory);

    private sealed record SignedEnvelope(string Payload, string Signature);
}
