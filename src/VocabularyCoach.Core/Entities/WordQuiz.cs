namespace VocabularyCoach.Core.Entities;

/// <summary>一个释义选项。</summary>
public sealed record WordChoice(string DefinitionZh, bool IsCorrect);

/// <summary>卡片答题模式的问题：正确释义 + 干扰项，顺序已打乱。</summary>
public sealed record WordQuiz(IReadOnlyList<WordChoice> Choices);
