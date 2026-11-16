namespace VocabularyCoach.Core.Entities;

/// <summary>
/// 浮动卡片需要展示的单词聚合。Sprint 1 由内存示例词源提供，
/// Sprint 2 起由 SQLite 词库映射得到，UI 无需感知数据来源。
/// </summary>
public sealed record WordCard
{
    /// <summary>数据库词条 Id；0 表示未持久化（测试或内存卡片）。</summary>
    public int Id { get; init; }

    /// <summary>单词原文，必填。</summary>
    public required string Word { get; init; }

    /// <summary>音标原文，可为空（不含斜杠）。</summary>
    public string? Phonetic { get; init; }

    /// <summary>难度等级，数值越大越难。</summary>
    public int Difficulty { get; init; }

    /// <summary>词频排名，数值越小越常见；未知时为空。</summary>
    public int? FrequencyRank { get; init; }

    /// <summary>按展示优先级排列的释义集合。</summary>
    public IReadOnlyList<WordDefinition> Definitions { get; init; } = [];

    /// <summary>可选的 AI 增强内容（例句/简释/辨析）；null 表示未生成或 AI 关闭。</summary>
    public WordEnrichment? Enrichment { get; init; }

    /// <summary>选择释义模式下的题目；null 表示使用自我评估（认识/模糊/不认识）。</summary>
    public WordQuiz? Quiz { get; init; }

    /// <summary>卡片默认展示的主释义。</summary>
    public WordDefinition? PrimaryDefinition =>
        Definitions.Count > 0 ? Definitions[0] : null;

    /// <summary>是否包含可展开的释义内容。</summary>
    public bool HasDetails => PrimaryDefinition is not null;

    /// <summary>带斜杠的音标展示文本，例如 /ˈæpl/；无音标时返回 null。</summary>
    public string? DisplayPhonetic
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Phonetic))
            {
                return null;
            }

            return $"/{Phonetic.Trim().Trim('/').Trim()}/";
        }
    }
}
