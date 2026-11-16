using VocabularyCoach.Application.Review;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Tests.Review;

public class Sm2ReviewSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly Sm2ReviewScheduler _scheduler = new();

    [Fact]
    public void FirstGood_LearnsAgainTomorrow()
    {
        var result = _scheduler.Schedule(ReviewState.New, ReviewRating.Good, Now);

        Assert.Equal(1, result.Repetition);
        Assert.Equal(1, result.IntervalDays, 3);
        Assert.Equal(2.5, result.EaseFactor, 3);
        Assert.Equal(WordProgressStatus.Learning, result.Status);
        Assert.Equal(Now.AddDays(1), result.DueAt);
    }

    [Fact]
    public void SecondGood_ExtendsToThreeDays()
    {
        var state = new ReviewState { Repetition = 1, IntervalDays = 1, EaseFactor = 2.5 };

        var result = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.Equal(2, result.Repetition);
        Assert.Equal(3, result.IntervalDays, 3);
        Assert.Equal(WordProgressStatus.Review, result.Status);
    }

    [Fact]
    public void ThirdGood_MultipliesByEaseFactor()
    {
        var state = new ReviewState { Repetition = 2, IntervalDays = 3, EaseFactor = 2.5 };

        var result = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.Equal(3, result.Repetition);
        Assert.Equal(7.5, result.IntervalDays, 3);
        Assert.Equal(Now.AddDays(7.5), result.DueAt);
    }

    [Fact]
    public void Again_ResetsRepetition_AndSchedulesQuickRelearn()
    {
        var state = new ReviewState
        {
            Repetition = 5,
            IntervalDays = 30,
            EaseFactor = 2.5,
            LapseCount = 1,
        };

        var result = _scheduler.Schedule(state, ReviewRating.Again, Now);

        Assert.Equal(0, result.Repetition);
        Assert.Equal(2, result.LapseCount);
        Assert.Equal(2.3, result.EaseFactor, 3);
        Assert.Equal(10.0 / (24 * 60), result.IntervalDays, 5);
        Assert.Equal(WordProgressStatus.Learning, result.Status);
        Assert.Equal(Now.AddMinutes(10), result.DueAt);
    }

    [Fact]
    public void Hard_UsesShorterIntervalThanGood()
    {
        var state = new ReviewState { Repetition = 4, IntervalDays = 20, EaseFactor = 2.5 };

        var hard = _scheduler.Schedule(state, ReviewRating.Hard, Now);
        var good = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.True(hard.IntervalDays < good.IntervalDays);
        Assert.Equal(24, hard.IntervalDays, 3);
        Assert.Equal(2.35, hard.EaseFactor, 3);
        Assert.Equal(WordProgressStatus.Review, hard.Status);
    }

    [Fact]
    public void Hard_FromNewWord_SchedulesOneDay()
    {
        var result = _scheduler.Schedule(ReviewState.New, ReviewRating.Hard, Now);

        Assert.Equal(1, result.Repetition);
        Assert.Equal(1, result.IntervalDays, 3);
        Assert.Equal(WordProgressStatus.Learning, result.Status);
    }

    [Fact]
    public void Easy_GrowsFasterThanGood()
    {
        var state = new ReviewState { Repetition = 1, IntervalDays = 1, EaseFactor = 2.5 };

        var easy = _scheduler.Schedule(state, ReviewRating.Easy, Now);
        var good = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.True(easy.IntervalDays > good.IntervalDays);
        Assert.Equal(2.65, easy.EaseFactor, 3);
    }

    [Fact]
    public void EaseFactor_NeverDropsBelowMinimum()
    {
        var state = new ReviewState { Repetition = 3, IntervalDays = 5, EaseFactor = 1.35 };

        var result = _scheduler.Schedule(state, ReviewRating.Again, Now);

        Assert.Equal(Sm2ReviewScheduler.MinEaseFactor, result.EaseFactor, 3);
    }

    [Fact]
    public void EaseFactor_NeverExceedsMaximum()
    {
        var state = new ReviewState { Repetition = 3, IntervalDays = 5, EaseFactor = 2.95 };

        var result = _scheduler.Schedule(state, ReviewRating.Easy, Now);

        Assert.Equal(Sm2ReviewScheduler.MaxEaseFactor, result.EaseFactor, 3);
    }

    [Fact]
    public void LongInterval_BecomesMastered()
    {
        var state = new ReviewState { Repetition = 5, IntervalDays = 30, EaseFactor = 2.5 };

        var result = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.Equal(75, result.IntervalDays, 3);
        Assert.Equal(WordProgressStatus.Mastered, result.Status);
    }

    [Fact]
    public void Interval_IsClampedToMaximum()
    {
        var state = new ReviewState { Repetition = 10, IntervalDays = 300, EaseFactor = 3.0 };

        var result = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.Equal(Sm2ReviewScheduler.MaxIntervalDays, result.IntervalDays, 3);
        Assert.Equal(Now.AddDays(Sm2ReviewScheduler.MaxIntervalDays), result.DueAt);
    }

    [Fact]
    public void InvalidEaseFactor_FallsBackToDefault()
    {
        var state = new ReviewState { Repetition = 3, IntervalDays = 3, EaseFactor = 0 };

        var result = _scheduler.Schedule(state, ReviewRating.Good, Now);

        Assert.Equal(Sm2ReviewScheduler.DefaultEaseFactor, result.EaseFactor, 3);
    }

    [Fact]
    public void Result_KeepsRating()
    {
        var result = _scheduler.Schedule(ReviewState.New, ReviewRating.Hard, Now);

        Assert.Equal(ReviewRating.Hard, result.Rating);
    }
}
