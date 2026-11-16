using VocabularyCoach.Core.Enums;
using VocabularyCoach.Core.Extensions;

namespace VocabularyCoach.Core.Tests.Extensions;

public class WordCardResultExtensionsTests
{
    [Theory]
    [InlineData(WordCardResult.Recognized, WordFeedback.Recognized)]
    [InlineData(WordCardResult.Vague, WordFeedback.Vague)]
    [InlineData(WordCardResult.Unknown, WordFeedback.Unknown)]
    public void ToFeedbackOrNull_MapsMasteryResults(WordCardResult result, WordFeedback expected)
    {
        Assert.Equal(expected, result.ToFeedbackOrNull());
        Assert.True(result.IsMasteryFeedback());
    }

    [Theory]
    [InlineData(WordCardResult.Dismissed)]
    [InlineData(WordCardResult.Snoozed)]
    [InlineData(WordCardResult.Timeout)]
    public void ToFeedbackOrNull_ReturnsNullForNonMasteryResults(WordCardResult result)
    {
        Assert.Null(result.ToFeedbackOrNull());
        Assert.False(result.IsMasteryFeedback());
    }
}
