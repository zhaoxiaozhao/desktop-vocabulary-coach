using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Application.Tests.Scheduling;

public class SchedulerOptionsTests
{
    [Fact]
    public void NextInterval_StaysWithinConfiguredBounds()
    {
        var options = new SchedulerOptions { MinIntervalMinutes = 12, MaxIntervalMinutes = 35 };
        var random = new Random(12345);

        for (var i = 0; i < 200; i++)
        {
            var interval = options.NextInterval(random);
            Assert.InRange(interval.TotalMinutes, 12, 35);
        }
    }

    [Fact]
    public void NextInterval_SupportsSubMinuteIntervalsForTesting()
    {
        var options = new SchedulerOptions { MinIntervalMinutes = 0.002, MaxIntervalMinutes = 0.003 };
        var interval = options.NextInterval(new Random(1));

        Assert.InRange(interval.TotalMilliseconds, 100, 200);
    }

    [Fact]
    public void NextInterval_ToleratesInvertedBounds()
    {
        var options = new SchedulerOptions { MinIntervalMinutes = 30, MaxIntervalMinutes = 10 };
        var interval = options.NextInterval(new Random(1));

        Assert.InRange(interval.TotalMinutes, 10, 30);
    }

    [Theory]
    [InlineData("22:30", "08:00", true)]
    [InlineData("08:00", "22:30", true)]
    [InlineData("00:00", "00:00", true)]
    [InlineData("25:00", "08:00", false)]
    [InlineData("22:30", "not-a-time", false)]
    [InlineData("", "", false)]
    public void HasValidQuietHours_ValidatesFormat(string start, string end, bool expected)
    {
        var options = new SchedulerOptions { QuietHoursStart = start, QuietHoursEnd = end };

        Assert.Equal(expected, options.HasValidQuietHours);
    }

    [Fact]
    public void GetQuietHours_ReturnsDisabledOnInvalidFormat()
    {
        var options = new SchedulerOptions { QuietHoursStart = "oops", QuietHoursEnd = "08:00" };

        Assert.Equal(QuietHours.Disabled, options.GetQuietHours());
    }

    [Fact]
    public void GetQuietHours_ParsesConfiguredWindow()
    {
        var options = new SchedulerOptions { QuietHoursStart = "22:30", QuietHoursEnd = "08:00" };
        var quietHours = options.GetQuietHours();

        Assert.True(quietHours.IsQuiet(new TimeOnly(23, 0)));
        Assert.False(quietHours.IsQuiet(new TimeOnly(12, 0)));
    }
}
