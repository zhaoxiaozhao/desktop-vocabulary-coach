namespace VocabularyCoach.Application.Settings;

/// <summary>
/// AI Provider 预设。全部使用 OpenAI-compatible Chat Completions 协议，
/// 覆盖中美主流服务；Custom 允许填写任意兼容地址。
/// </summary>
public enum AiPreset
{
    OpenAi = 1,
    Anthropic = 2,
    Gemini = 3,
    DeepSeek = 4,
    Moonshot = 5,
    Qwen = 6,
    Zhipu = 7,
    SiliconFlow = 8,
    Custom = 9,
    Mock = 10,
    OpenCodeGo = 11,
}

/// <summary>Provider 预设信息（默认 Base URL 与模型仅作初值，可修改）。</summary>
public sealed record AiPresetInfo(AiPreset Preset, string Name, string BaseUrl, string DefaultModel);

/// <summary>中美主流 OpenAI-compatible 服务预设。</summary>
public static class AiPresetCatalog
{
    public static IReadOnlyList<AiPresetInfo> All { get; } =
    [
        new(AiPreset.OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini"),
        new(AiPreset.Anthropic, "Anthropic Claude", "https://api.anthropic.com/v1", "claude-3-5-haiku-latest"),
        new(AiPreset.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.0-flash"),
        new(AiPreset.DeepSeek, "DeepSeek 深度求索", "https://api.deepseek.com/v1", "deepseek-chat"),
        new(AiPreset.Moonshot, "Moonshot Kimi", "https://api.moonshot.cn/v1", "moonshot-v1-8k"),
        new(AiPreset.Qwen, "阿里通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
        new(AiPreset.Zhipu, "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
        new(AiPreset.SiliconFlow, "SiliconFlow 硅基流动", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct"),
        new(AiPreset.Custom, "自定义（OpenAI-compatible）", string.Empty, string.Empty),
        new(AiPreset.OpenCodeGo, "OpenCode Go（需订阅）", "https://opencode.ai/inference/go/v1", "glm-5.3-flash"),
        new(AiPreset.Mock, "Mock（离线示例，无需 Key）", string.Empty, "mock"),
    ];

    public static AiPresetInfo Get(AiPreset preset) =>
        All.FirstOrDefault(x => x.Preset == preset) ?? All[^1];
}
