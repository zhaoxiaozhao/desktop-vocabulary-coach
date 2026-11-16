namespace VocabularyCoach.Core.Entities;

/// <summary>题目方向：看词选义（正向）或看义选词（反向）。</summary>
public enum QuizDirection
{
    /// <summary>看到英文单词，选择正确的中文释义。</summary>
    WordToDefinition = 1,

    /// <summary>看到中文释义，选择正确的英文单词。</summary>
    DefinitionToWord = 2,
}

/// <summary>一个选择题选项；Text 是选项内容（中文释义或英文单词）。</summary>
public sealed record WordChoice(string Text, bool IsCorrect);

/// <summary>卡片选择题：正确选项 + 干扰项，顺序已打乱。</summary>
public sealed record WordQuiz(
    IReadOnlyList<WordChoice> Choices,
    QuizDirection Direction = QuizDirection.WordToDefinition)
{
    public bool IsReverse => Direction == QuizDirection.DefinitionToWord;
}
