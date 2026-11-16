namespace VocabularyCoach.Core.Entities;

/// <summary>
/// 单词的一条释义。一个单词允许多条释义，卡片默认展示排序后的第一条。
/// </summary>
public sealed record WordDefinition
{
    /// <summary>词性，例如 n. / v. / adj.，可为空。</summary>
    public string? PartOfSpeech { get; init; }

    /// <summary>中文释义，必填。</summary>
    public required string DefinitionZh { get; init; }

    /// <summary>英文释义，可为空。</summary>
    public string? DefinitionEn { get; init; }

    /// <summary>英文例句，可为空。</summary>
    public string? ExampleEn { get; init; }

    /// <summary>例句中文翻译，可为空。</summary>
    public string? ExampleZh { get; init; }

    /// <summary>是否包含例句，用于卡片决定是否显示例句区域。</summary>
    public bool HasExample => !string.IsNullOrWhiteSpace(ExampleEn);
}
