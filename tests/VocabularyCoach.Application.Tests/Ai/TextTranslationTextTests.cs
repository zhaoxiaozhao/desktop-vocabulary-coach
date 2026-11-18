using VocabularyCoach.Application.Ai;

namespace VocabularyCoach.Application.Tests.Ai;

public class TextTranslationTextTests
{
    [Theory]
    [InlineData("你好，世界", true)]
    [InlineData("hello 世界", true)]
    [InlineData("hello world", false)]
    [InlineData("", false)]
    public void ContainsChinese_DetectsCjkText(string text, bool expected)
    {
        Assert.Equal(expected, TextTranslationText.ContainsChinese(text));
    }

    [Theory]
    [InlineData("\"Hello world\"", "Hello world")]
    [InlineData("“你好”", "你好")]
    [InlineData("英文：Hello world", "Hello world")]
    [InlineData("Translation: Hello world", "Hello world")]
    [InlineData("```english\nHello world\n```", "Hello world")]
    [InlineData("  Hello world  ", "Hello world")]
    public void Clean_StripsCommonModelWrappers(string input, string expected)
    {
        Assert.Equal(expected, TextTranslationText.Clean(input));
    }

    [Fact]
    public void Clean_ReturnsEmpty_ForBlankInput()
    {
        Assert.Equal(string.Empty, TextTranslationText.Clean("   "));
        Assert.Equal(string.Empty, TextTranslationText.Clean(null));
    }
}
