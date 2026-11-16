using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Tests.Support;

namespace VocabularyCoach.Application.Tests.Scheduling;

public class PauseStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewInstance_IsNotPaused()
    {
        var state = CreateState(out _);

        Assert.False(state.IsPaused);
        Assert.Null(state.PausedUntil);
    }

    [Fact]
    public void Pause_IsActiveUntilDurationElapses()
    {
        var state = CreateState(out var clock);

        state.Pause(TimeSpan.FromMinutes(30));

        Assert.True(state.IsPaused);
        Assert.Equal(Start.AddMinutes(30), state.PausedUntil);

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(state.IsPaused);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(state.IsPaused);
        Assert.Null(state.PausedUntil);
    }

    [Fact]
    public void Resume_ClearsPauseImmediately()
    {
        var state = CreateState(out _);
        state.Pause(TimeSpan.FromHours(1));

        state.Resume();

        Assert.False(state.IsPaused);
        Assert.Null(state.PausedUntil);
    }

    [Fact]
    public void PauseUntilTomorrow_LastsUntilNextMidnight()
    {
        var state = CreateState(out _);

        state.PauseUntilTomorrow();

        Assert.True(state.IsPaused);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero), state.PausedUntil);
    }

    [Fact]
    public void PauseUntilTomorrow_UsesConfiguredResumeTime()
    {
        var state = CreateState(out _);

        state.PauseUntilTomorrow(new TimeOnly(8, 30));

        Assert.True(state.IsPaused);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 8, 30, 0, TimeSpan.Zero), state.PausedUntil);
    }

    [Fact]
    public void Pause_RejectsNonPositiveDuration()
    {
        var state = CreateState(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Pause(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Pause(TimeSpan.FromSeconds(-1)));
    }

    private static PauseState CreateState(out FakeTimeProvider clock)
    {
        clock = new FakeTimeProvider(Start);
        return new PauseState(clock);
    }
}
