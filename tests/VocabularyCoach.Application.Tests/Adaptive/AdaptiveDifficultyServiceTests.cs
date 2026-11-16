using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Tests.Adaptive;

public class AdaptiveDifficultyServiceTests
{
    [Fact]
    public async Task HighRecognition_RaisesDifficultyAndRatio()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 2 },
            RecentMasteryFeedback = Repeat(WordFeedback.Recognized, 17)
                .Concat(Repeat(WordFeedback.Vague, 3))
                .ToList(),
        };

        var service = Create(store, new UserSettings { DifficultyMode = DifficultyMode.Adaptive });
        var difficulty = await service.ApplyAsync(CancellationToken.None);

        Assert.Equal(3, difficulty);
        Assert.Equal(3, store.Profile.CurrentDifficulty);
        Assert.Equal(0.65, store.Profile.PreferredNewWordRatio!.Value, 3);
        Assert.Equal(20, store.Profile.RecentSampleSize);
        Assert.Equal(17, store.Profile.RecentRecognized);
        Assert.Equal(3, store.Profile.RecentVague);
        Assert.Equal(0, store.Profile.RecentUnknown);
    }

    [Fact]
    public async Task HighUnknown_LowersDifficultyAndReviewTendency()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 3, PreferredNewWordRatio = 0.6 },
            RecentMasteryFeedback = Repeat(WordFeedback.Unknown, 8)
                .Concat(Repeat(WordFeedback.Vague, 6))
                .Concat(Repeat(WordFeedback.Recognized, 6))
                .ToList(),
        };

        var service = Create(store, new UserSettings { DifficultyMode = DifficultyMode.Adaptive });
        var difficulty = await service.ApplyAsync(CancellationToken.None);

        Assert.Equal(2, difficulty);
        Assert.Equal(0.5, store.Profile.PreferredNewWordRatio!.Value, 3);
    }

    [Fact]
    public async Task InsufficientSample_DoesNotAdjustDifficulty()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 2 },
            RecentMasteryFeedback = Repeat(WordFeedback.Recognized, 5),
        };

        var service = Create(store, new UserSettings { DifficultyMode = DifficultyMode.Adaptive });
        var difficulty = await service.ApplyAsync(CancellationToken.None);

        Assert.Equal(2, difficulty);
        Assert.Null(store.Profile.PreferredNewWordRatio);
        Assert.Equal(5, store.Profile.RecentSampleSize);
    }

    [Fact]
    public async Task ManualMode_OnlyUpdatesStatistics()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 2 },
            RecentMasteryFeedback = Repeat(WordFeedback.Recognized, 20),
        };

        var service = Create(store, new UserSettings { DifficultyMode = DifficultyMode.Manual });
        var difficulty = await service.ApplyAsync(CancellationToken.None);

        Assert.Equal(2, difficulty);
        Assert.Equal(20, store.Profile.RecentSampleSize);
    }

    [Fact]
    public async Task Difficulty_IsClampedAtUpperBound()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 4 },
            RecentMasteryFeedback = Repeat(WordFeedback.Recognized, 20),
        };

        var service = Create(store, new UserSettings { DifficultyMode = DifficultyMode.Adaptive });
        var difficulty = await service.ApplyAsync(CancellationToken.None);

        Assert.Equal(4, difficulty);
    }

    [Fact]
    public async Task Difficulty_IsClampedAtLowerBound()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot { CurrentDifficulty = 1 },
            RecentMasteryFeedback = Repeat(WordFeedback.Unknown, 20),
        };

        var service = Create(store, new UserSettings { DifficultyMode = DifficultyMode.Adaptive });
        var difficulty = await service.ApplyAsync(CancellationToken.None);

        Assert.Equal(1, difficulty);
    }

    [Fact]
    public async Task ResetPreference_ClearsAdaptiveRatio()
    {
        var store = new RecordingLearningStore
        {
            Profile = new LearningProfileSnapshot
            {
                CurrentDifficulty = 3,
                PreferredNewWordRatio = 0.45,
                RecentSampleSize = 20,
            },
        };

        var service = Create(store, new UserSettings { NewWordRatio = 0.7 });
        await service.ResetPreferenceAsync(CancellationToken.None);

        Assert.Null(store.Profile.PreferredNewWordRatio);
        Assert.Equal(3, store.Profile.CurrentDifficulty);
        Assert.Equal(20, store.Profile.RecentSampleSize);
    }

    private static AdaptiveDifficultyService Create(RecordingLearningStore store, UserSettings settings) =>
        new(
            store,
            new FakeSettingsService { Current = settings },
            NullLogger<AdaptiveDifficultyService>.Instance);

    private static List<WordFeedback> Repeat(WordFeedback feedback, int count) =>
        Enumerable.Repeat(feedback, count).ToList();
}
