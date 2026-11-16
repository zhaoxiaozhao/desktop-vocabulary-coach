namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>WordDefinitions 表：一个单词可以有多条释义。</summary>
public sealed class WordDefinitionRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public WordRecord? Word { get; set; }

    public string? PartOfSpeech { get; set; }

    public string DefinitionZh { get; set; } = string.Empty;

    public string? DefinitionEn { get; set; }

    public string? ExampleEn { get; set; }

    public string? ExampleZh { get; set; }

    /// <summary>展示顺序，数值越小越靠前。</summary>
    public int SortOrder { get; set; }
}
