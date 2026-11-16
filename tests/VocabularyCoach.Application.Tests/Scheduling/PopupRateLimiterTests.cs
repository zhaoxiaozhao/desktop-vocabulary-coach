using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Tests.Support;

namespace VocabularyCoach.Application.Tests.Scheduling;

public class PopupRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CanShow_AllowsUpToLimit()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = CreateLimiter(clock, maxPerHour: 3);

        Assert.True(limiter.CanShow());
        limiter.RecordPopup(clock.GetUtcNow());
        limiter.RecordPopup(clock.GetUtcNow());
        Assert.True(limiter.CanShow());

        limiter.RecordPopup(clock.GetUtcNow());
        Assert.False(limiter.CanShow());
        Assert.Equal(3, limiter.CountInLastHour());
    }

    [Fact]
    public void Window_SlidesAfterOneHour()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = CreateLimiter(clock, maxPerHour: 1);

        limiter.RecordPopup(clock.GetUtcNow());
        Assert.False(limiter.CanShow());

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(limiter.CanShow());

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(limiter.CanShow());
        Assert.Equal(0, limiter.CountInLastHour());
    }

    [Fact]
    public void RecordPopup_UsesProvidedTimestamp()
    {
        var clock = new FakeTimeProvider(Start);
        var limiter = CreateLimiter(clock, maxPerHour: 2);

        limiter.RecordPopup(Start.AddHours(-2));
        limiter.RecordPopup(Start);

        Assert.Equal(1, limiter.CountInLastHour());
    }

    private static PopupRateLimiter CreateLimiter(FakeTimeProvider clock, int maxPerHour) =>
        new(
            new FakeSettingsService
            {
                Scheduler = new SchedulerOptions { MaxPopupsPerHour = maxPerHour },
            },
            clock);
}
