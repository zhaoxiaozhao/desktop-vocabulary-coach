using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.Application.SentencePractice;

/// <summary>解析句子练习实际使用的模型，并识别推理模型。</summary>
public static class SentenceModelHelper
{
    public static string ResolveModel(UserSettings settings) =>
        string.IsNullOrWhiteSpace(settings.SentencePracticeModel)
            ? settings.AiModel
            : settings.SentencePracticeModel.Trim();

    public static bool IsReasoningModel(string model)
    {
        var name = model.Trim().ToLowerInvariant();
        return name.Contains("glm-5")
            || name.Contains("glm-4.5")
            || name.Contains("reasoner")
            || name.Contains("deepseek-r1")
            || name.StartsWith("o1")
            || name.StartsWith("o3");
    }
}
