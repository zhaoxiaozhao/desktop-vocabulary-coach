namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>Words 表：词条主数据。用户进度不在此表，避免词库更新覆盖学习记录。</summary>
public sealed class WordRecord
{
    public int Id { get; set; }

    public string Word { get; set; } = string.Empty;

    /// <summary>规范化单词（小写、去首尾空白），唯一索引。</summary>
    public string NormalizedWord { get; set; } = string.Empty;

    public string? Phonetic { get; set; }

    /// <summary>难度等级 1-4，数值越大越难。</summary>
    public int Difficulty { get; set; }

    /// <summary>词频排名，数值越小越常见。</summary>
    public int? FrequencyRank { get; set; }

    public string Source { get; set; } = "ECDICT";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<WordDefinitionRecord> Definitions { get; set; } = [];
}
