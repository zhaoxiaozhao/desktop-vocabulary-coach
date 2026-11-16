using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Abstractions;

/// <summary>
/// 本地词库查询能力。UI 与推荐逻辑通过该接口访问单词，不直接依赖 EF Core。
/// </summary>
public interface IWordLibrary
{
    /// <summary>按单词查询（大小写、首尾空白不敏感）。找不到返回 null。</summary>
    Task<WordCard?> FindByWordAsync(string word, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取一个适合学习的未学新词：优先选择难度不超过指定值的词，
    /// 在难度和词频靠前的候选里随机挑选，避免顺序背词。
    /// </summary>
    Task<WordCard?> GetNextNewWordAsync(int maxDifficulty, CancellationToken cancellationToken = default);

    /// <summary>词库单词总数。</summary>
    Task<int> GetWordCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 取若干来自其他单词的中文释义作为选择题干扰项。
    /// 优先相同难度，数量不足时放宽。
    /// </summary>
    Task<IReadOnlyList<string>> GetDistractorDefinitionsAsync(
        int excludeWordId,
        int count,
        int preferredDifficulty,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取若干其他单词的英文原文，用于「看义选词」反向选择题的干扰项。
    /// 会排除中文释义与正确释义完全相同的单词，避免出现多个正确答案。
    /// </summary>
    Task<IReadOnlyList<string>> GetDistractorWordsAsync(
        int excludeWordId,
        string correctDefinition,
        int preferredLength,
        int count,
        int preferredDifficulty,
        CancellationToken cancellationToken = default);
}
