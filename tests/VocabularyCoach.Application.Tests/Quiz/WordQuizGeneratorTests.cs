using VocabularyCoach.Application.Quiz;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Quiz;

public class WordQuizGeneratorTests
{
    [Fact]
    public async Task TryCreateAsync_BuildsOneCorrectAndDistractors()
    {
        var library = new FakeWordLibrary
        {
            Distractors = ["干扰一", "干扰二", "干扰三", "干扰四"],
        };
        var generator = new WordQuizGenerator(library);
        var card = CardWithDefinition("resilient", "有韧性的");

        var quiz = await generator.TryCreateAsync(card, CancellationToken.None);

        Assert.NotNull(quiz);
        Assert.Equal(4, quiz!.Choices.Count);
        Assert.Single(quiz.Choices, x => x.IsCorrect);
        Assert.Equal("有韧性的", quiz.Choices.Single(x => x.IsCorrect).DefinitionZh);
        Assert.Equal(3, quiz.Choices.Count(x => !x.IsCorrect));
        Assert.Equal(4, quiz.Choices.Select(x => x.DefinitionZh).Distinct().Count());
    }

    [Fact]
    public async Task TryCreateAsync_ReturnsNull_WhenFewerThanThreeDistractors()
    {
        var library = new FakeWordLibrary { Distractors = ["干扰一", "干扰二"] };
        var generator = new WordQuizGenerator(library);

        Assert.Null(await generator.TryCreateAsync(CardWithDefinition("apple", "苹果"), CancellationToken.None));
    }

    [Fact]
    public async Task TryCreateAsync_ReturnsNull_WithoutDefinition()
    {
        var generator = new WordQuizGenerator(new FakeWordLibrary { Distractors = ["a", "b", "c"] });
        var card = new WordCard { Id = 1, Word = "apple" };

        Assert.Null(await generator.TryCreateAsync(card, CancellationToken.None));
    }

    [Fact]
    public async Task TryCreateAsync_IgnoresDistractorEqualToCorrectDefinition()
    {
        var library = new FakeWordLibrary { Distractors = ["苹果", "苹果树", "水果", "蔬菜"] };
        var generator = new WordQuizGenerator(library);

        var quiz = await generator.TryCreateAsync(CardWithDefinition("apple", "苹果"), CancellationToken.None);

        Assert.NotNull(quiz);
        Assert.Equal(4, quiz!.Choices.Count);
        Assert.Single(quiz.Choices, x => x.IsCorrect);
        Assert.DoesNotContain(quiz.Choices.Where(x => !x.IsCorrect), x => x.DefinitionZh == "苹果");
    }

    private static WordCard CardWithDefinition(string word, string definition) => new()
    {
        Id = 1,
        Word = word,
        Difficulty = 2,
        Definitions = [new WordDefinition { DefinitionZh = definition }],
    };
}
