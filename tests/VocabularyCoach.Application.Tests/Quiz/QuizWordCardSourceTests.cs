using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Quiz;
using VocabularyCoach.Application.Recommendation;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Quiz;

public class QuizWordCardSourceTests
{
    [Fact]
    public async Task SelfReportMode_ReturnsCardWithoutQuiz()
    {
        var generator = new FakeQuizGenerator { Quiz = SampleQuiz() };
        var source = CreateSource(InteractionMode.SelfReport, generator);

        var card = await source.GetNextCardAsync(CancellationToken.None);

        Assert.NotNull(card);
        Assert.Null(card!.Quiz);
        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task ChoiceQuizMode_AttachesQuiz()
    {
        var quiz = SampleQuiz();
        var generator = new FakeQuizGenerator { Quiz = quiz };
        var source = CreateSource(InteractionMode.ChoiceQuiz, generator);

        var card = await source.GetNextCardAsync(CancellationToken.None);

        Assert.NotNull(card);
        Assert.Same(quiz, card!.Quiz);
        Assert.Equal(1, generator.CallCount);
        Assert.Equal(QuizDirection.WordToDefinition, generator.LastDirection);
    }

    [Fact]
    public async Task ReverseQuizMode_RequestsReverseDirection()
    {
        var quiz = SampleQuiz(QuizDirection.DefinitionToWord);
        var generator = new FakeQuizGenerator { Quiz = quiz };
        var source = CreateSource(InteractionMode.ReverseQuiz, generator);

        var card = await source.GetNextCardAsync(CancellationToken.None);

        Assert.NotNull(card);
        Assert.Same(quiz, card!.Quiz);
        Assert.True(card.Quiz!.IsReverse);
        Assert.Equal(1, generator.CallCount);
        Assert.Equal(QuizDirection.DefinitionToWord, generator.LastDirection);
    }

    [Fact]
    public async Task ChoiceQuizMode_FallsBackWhenQuizUnavailable()
    {
        var generator = new FakeQuizGenerator { Quiz = null };
        var source = CreateSource(InteractionMode.ChoiceQuiz, generator);

        var card = await source.GetNextCardAsync(CancellationToken.None);

        Assert.NotNull(card);
        Assert.Null(card!.Quiz);
        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoWordAvailable()
    {
        var library = new FakeWordLibrary { NextNewWord = null };
        var settings = new FakeSettingsService { Current = new UserSettings { InteractionMode = InteractionMode.ChoiceQuiz } };
        var source = new QuizWordCardSource(
            CreateRecommender(library, settings),
            new FakeQuizGenerator { Quiz = SampleQuiz() },
            new FakeGeneratedSentenceStore(),
            settings,
            NullLogger<QuizWordCardSource>.Instance);

        Assert.Null(await source.GetNextCardAsync(CancellationToken.None));
    }

    private static QuizWordCardSource CreateSource(InteractionMode mode, FakeQuizGenerator generator)
    {
        var library = new FakeWordLibrary
        {
            NextNewWord = new WordCard
            {
                Id = 1,
                Word = "resilient",
                Difficulty = 2,
                Definitions = [new WordDefinition { DefinitionZh = "有韧性的" }],
            },
        };
        var settings = new FakeSettingsService { Current = new UserSettings { InteractionMode = mode } };

        return new QuizWordCardSource(
            CreateRecommender(library, settings),
            generator,
            new FakeGeneratedSentenceStore(),
            settings,
            NullLogger<QuizWordCardSource>.Instance);
    }

    private static WordRecommendationService CreateRecommender(
        FakeWordLibrary library,
        FakeSettingsService settings) =>
        new(
            new RecordingLearningStore(),
            library,
            settings,
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<WordRecommendationService>.Instance);

    private static WordQuiz SampleQuiz(
        QuizDirection direction = QuizDirection.WordToDefinition) => new(
    [
        new WordChoice(direction == QuizDirection.DefinitionToWord ? "resilient" : "有韧性的", true),
        new WordChoice(direction == QuizDirection.DefinitionToWord ? "ambiguous" : "模棱两可的", false),
        new WordChoice(direction == QuizDirection.DefinitionToWord ? "momentum" : "势头的", false),
        new WordChoice(direction == QuizDirection.DefinitionToWord ? "grateful" : "感激的", false),
    ],
    direction);

    private sealed class FakeQuizGenerator : IWordQuizGenerator
    {
        public WordQuiz? Quiz { get; set; }

        public int CallCount { get; private set; }

        public QuizDirection? LastDirection { get; private set; }

        public Task<WordQuiz?> TryCreateAsync(WordCard card, CancellationToken cancellationToken) =>
            TryCreateAsync(card, QuizDirection.WordToDefinition, cancellationToken);

        public Task<WordQuiz?> TryCreateAsync(
            WordCard card,
            QuizDirection direction,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDirection = direction;
            return Task.FromResult(Quiz);
        }
    }
}
