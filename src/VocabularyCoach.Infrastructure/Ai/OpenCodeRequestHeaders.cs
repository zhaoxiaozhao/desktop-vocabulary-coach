namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>
/// OpenCode 请求头：Go 端点要求稳定的 x-opencode-session，否则返回 400 MissingSessionID；
/// 同时带上可识别的 User-Agent，便于服务端路由。
/// </summary>
internal static class OpenCodeRequestHeaders
{
    private static readonly string SessionId = "dvc-" + Guid.NewGuid().ToString("N");

    public static void Apply(HttpRequestMessage message, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !baseUrl.Contains("opencode.ai", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        message.Headers.TryAddWithoutValidation(
            "User-Agent",
            "DesktopVocabularyCoach/0.9.1");

        if (baseUrl.Contains("opencode.ai/inference/go", StringComparison.OrdinalIgnoreCase))
        {
            message.Headers.TryAddWithoutValidation("x-opencode-session", SessionId);
        }
    }
}
