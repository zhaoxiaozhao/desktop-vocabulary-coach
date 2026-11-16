using VocabularyCoach.Application.Capture;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Capture;

public class WordTextParserTests
{
    [Theory]
    [InlineData("resilient", "resilient")]
    [InlineData("  Resilient.  ", "resilient")]
    [InlineData("“resilient”", "resilient")]
    [InlineData("(resilient)", "resilient")]
    [InlineData("don't", "don't")]
    [InlineData("well-known", "well-known")]
    public void TryParse_AcceptsSingleWords_AndTrimsPunctuation(string raw, string expected)
    {
        Assert.True(WordTextParser.TryParse(raw, out var parsed, out var error), error);
        Assert.Equal(expected, parsed.Normalized);
    }

    [Theory]
    [InlineData("take off", "take off")]
    [InlineData("  in   charge   of ", "in charge of")]
    [InlineData("a piece of cake", "a piece of cake")]
    public void TryParse_AcceptsPhrases_AndCollapsesWhitespace(string raw, string expected)
    {
        Assert.True(WordTextParser.TryParse(raw, out var parsed, out var error), error);
        Assert.Equal(expected, parsed.Normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("hello-world-123")]
    [InlineData("这是一个单词")]
    [InlineData("hello, world")]
    [InlineData("one two three four five six")]
    [InlineData("https://example.com")]
    public void TryParse_RejectsInvalidText(string raw)
    {
        Assert.False(WordTextParser.TryParse(raw, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_PreservesOriginalCaseForDisplay()
    {
        Assert.True(WordTextParser.TryParse("Take Off", out var parsed, out _));

        Assert.Equal("Take Off", parsed.Original);
        Assert.Equal("take off", parsed.Normalized);
    }
}
