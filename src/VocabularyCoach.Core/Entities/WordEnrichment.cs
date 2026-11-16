namespace VocabularyCoach.Core.Entities;

/// <summary>
/// AI 可选增强内容：更贴近水平的例句、简明解释与易混词辨析。
/// 本地词库始终是主数据来源，AI 内容缺失时不影响卡片展示。
/// </summary>
public sealed record WordEnrichment(
    string? ExampleEn,
    string? ExampleZh,
    string? ExplanationZh,
    string? ConfusableNote);
