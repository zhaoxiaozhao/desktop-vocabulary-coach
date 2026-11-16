using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Abstractions;

/// <summary>
/// 提供下一张要展示的单词卡片。Sprint 1 使用内存示例词源，
/// Sprint 2 起替换为 SQLite 词库实现。
/// </summary>
public interface IWordCardSource
{
    /// <summary>获取下一张卡片；没有可展示的单词时返回 null。</summary>
    Task<WordCard?> GetNextCardAsync(CancellationToken cancellationToken);
}
