using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Recommendation;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Recommendation;

public class WordRecommendationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PrefersSeverelyOverdueReview_EvenWhenNewWordRatioIsOne()
    {
        var store = new RecordingLearningStore
        {
            DueReviews = [new DueReview(Card(1, "due"), Now.AddHours(-10))],
        };
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(store, library, new RecommendationOptions { NewWordRatio = 1 });

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal("due", card!.Word);
        Assert.Equal(0, library.NewWordCallCount);
    }

    [Fact]
    public async Task ReturnsNewWord_WhenNoReviewIsDue()
    {
        var store = new RecordingLearningStore();
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(store, library);

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal("new", card!.Word);
        Assert.Equal(1, library.NewWordCallCount);
    }

    [Fact]
    public async Task MixesNewWord_WhenPressureIsLowAndOverdueIsRecent()
    {
        var store = new RecordingLearningStore
        {
            DueReviews = [new DueReview(Card(1, "due"), Now.AddHours(-1))],
        };
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(
            store,
            library,
            new RecommendationOptions
            {
                NewWordRatio = 1,
                OverdueGraceHours = 6,
                ReviewPressureThreshold = 2,
            });

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal("new", card!.Word);
    }

    [Fact]
    public async Task PrefersReview_WhenQueuePressureIsHigh()
    {
        var store = new RecordingLearningStore
        {
            DueReviews =
            [
                new DueReview(Card(1, "due-1"), Now.AddHours(-1)),
                new DueReview(Card(2, "due-2"), Now.AddHours(-2)),
                new DueReview(Card(3, "due-3"), Now.AddHours(-3)),
            ],
        };
        var library = new FakeWordLibrary { NextNewWord = Card(9, "new") };
        var service = Create(
            store,
            library,
            new RecommendationOptions
            {
                NewWordRatio = 1,
                ReviewPressureThreshold = 2,
            });

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal("due-1", card!.Word);
        Assert.Equal(0, library.NewWordCallCount);
    }

    [Fact]
    public async Task UsesReviewStateDifficulty_ForNewWords()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 3 },
        };
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(store, library);

        await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal(1, library.NewWordCallCount);
        Assert.Equal(4, library.LastMaxDifficulty);
    }

    [Fact]
    public async Task ManualDifficultyMode_UsesConfiguredLevel()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 1 },
        };
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(
            store,
            library,
            userSettings: new UserSettings { DifficultyMode = DifficultyMode.Manual, ManualDifficulty = 3 });

        await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal(3, library.LastMaxDifficulty);
    }

    [Fact]
    public async Task NewWordsOnlyMode_IgnoresDueReviews()
    {
        var store = new RecordingLearningStore
        {
            DueReviews = [new DueReview(Card(1, "due"), Now.AddDays(-3))],
        };
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(
            store,
            library,
            userSettings: new UserSettings { RecommendationMode = RecommendationMode.NewWordsOnly });

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal("new", card!.Word);
        Assert.Equal(1, library.NewWordCallCount);
    }

    [Fact]
    public async Task ReviewOnlyMode_ReturnsNull_WhenNoReviewIsDue()
    {
        var store = new RecordingLearningStore();
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(
            store,
            library,
            userSettings: new UserSettings { RecommendationMode = RecommendationMode.ReviewOnly });

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Null(card);
        Assert.Equal(0, library.NewWordCallCount);
    }

    [Fact]
    public async Task ReviewOnlyMode_ReturnsDueReview()
    {
        var store = new RecordingLearningStore
        {
            DueReviews = [new DueReview(Card(1, "due"), Now.AddHours(-1))],
        };
        var library = new FakeWordLibrary { NextNewWord = Card(2, "new") };
        var service = Create(
            store,
            library,
            userSettings: new UserSettings { RecommendationMode = RecommendationMode.ReviewOnly });

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Equal("due", card!.Word);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoWordsAvailable()
    {
        var service = Create(new RecordingLearningStore(), new FakeWordLibrary());

        var card = await service.GetNextCardAsync(CancellationToken.None);

        Assert.Null(card);
    }

    private static WordRecommendationService Create(
        RecordingLearningStore store,
        FakeWordLibrary library,
        RecommendationOptions? options = null,
        UserSettings? userSettings = null)
    {
        var recommendationOptions = options ?? new RecommendationOptions();
        var user = userSettings ?? new UserSettings
        {
            NewWordRatio = recommendationOptions.NewWordRatio,
        };
        var settings = new FakeSettingsService
        {
            Current = user,
            Recommendation = recommendationOptions,
        };

        return new WordRecommendationService(
            store,
            library,
            settings,
            new FakeTimeProvider(Now),
            NullLogger<WordRecommendationService>.Instance);
    }

    private static WordCard Card(int id, string word) => new()
    {
        Id = id,
        Word = word,
        Definitions = [new WordDefinition { DefinitionZh = "释义" }],
    };
}
