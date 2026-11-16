using VocabularyCoach.Core.Enums;
using VocabularyCoach.Core.Extensions;

namespace VocabularyCoach.Core.Tests.Extensions;

public class WordFeedbackExtensionsTests
{
    [Theory]
    [InlineData(WordFeedback.Recognized, ReviewRating.Good)]
    [InlineData(WordFeedback.Vague, ReviewRating.Hard)]
    [InlineData(WordFeedback.Unknown, ReviewRating.Again)]
    public void ToReviewRating_MapsFeedback(WordFeedback feedback, ReviewRating expected)
    {
        Assert.Equal(expected, feedback.ToReviewRating());
    }
}
