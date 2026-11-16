using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Core.Tests.Entities;

public class WordCardTests
{
    [Fact]
    public void DisplayPhonetic_WrapsPhoneticInSlashes()
    {
        var card = new WordCard { Word = "apple", Phonetic = "ˈæpl" };

        Assert.Equal("/ˈæpl/", card.DisplayPhonetic);
    }

    [Theory]
    [InlineData("/ˈæpl/")]
    [InlineData(" ˈæpl ")]
    [InlineData("/ˈæpl")]
    public void DisplayPhonetic_NormalizesExistingSlashesAndWhitespace(string phonetic)
    {
        var card = new WordCard { Word = "apple", Phonetic = phonetic };

        Assert.Equal("/ˈæpl/", card.DisplayPhonetic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayPhonetic_ReturnsNullWhenMissing(string? phonetic)
    {
        var card = new WordCard { Word = "apple", Phonetic = phonetic };

        Assert.Null(card.DisplayPhonetic);
    }

    [Fact]
    public void PrimaryDefinition_UsesFirstDefinitionInList()
    {
        var first = new WordDefinition { PartOfSpeech = "n.", DefinitionZh = "苹果" };
        var second = new WordDefinition { PartOfSpeech = "n.", DefinitionZh = "苹果树" };
        var card = new WordCard
        {
            Word = "apple",
            Definitions = [first, second],
        };

        Assert.Same(first, card.PrimaryDefinition);
        Assert.True(card.HasDetails);
    }

    [Fact]
    public void HasDetails_IsFalseWithoutDefinitions()
    {
        var card = new WordCard { Word = "apple" };

        Assert.False(card.HasDetails);
        Assert.Null(card.PrimaryDefinition);
    }
}
