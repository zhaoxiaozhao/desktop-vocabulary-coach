namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>WordCaptures 表：记录用户捕获过的生词/短语（不保存来源应用）。</summary>
public sealed class WordCaptureRecord
{
    public int Id { get; set; }

    public int WordId { get; set; }

    public WordRecord? Word { get; set; }

    /// <summary>首次/最近一次捕获的原文。</summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>规范化文本（小写、空白折叠），唯一索引。</summary>
    public string NormalizedWord { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>累计捕获次数。</summary>
    public int CaptureCount { get; set; } = 1;
}
