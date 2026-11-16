using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Core.Tests.ValueObjects;

public class QuietHoursTests
{
    [Theory]
    [InlineData(23, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 59)]
    public void CrossMidnightWindow_IsQuietInsideRange(int hour, int minute)
    {
        var quietHours = new QuietHours(new TimeOnly(22, 30), new TimeOnly(8, 0));

        Assert.True(quietHours.IsQuiet(new TimeOnly(hour, minute)));
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    [InlineData(22, 29)]
    public void CrossMidnightWindow_IsNotQuietOutsideRange(int hour, int minute)
    {
        var quietHours = new QuietHours(new TimeOnly(22, 30), new TimeOnly(8, 0));

        Assert.False(quietHours.IsQuiet(new TimeOnly(hour, minute)));
    }

    [Theory]
    [InlineData(9, 0, true)]
    [InlineData(16, 59, true)]
    [InlineData(8, 59, false)]
    [InlineData(17, 0, false)]
    public void SameDayWindow_RespectsBoundaries(int hour, int minute, bool expected)
    {
        var quietHours = new QuietHours(new TimeOnly(9, 0), new TimeOnly(17, 0));

        Assert.Equal(expected, quietHours.IsQuiet(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void DisabledQuietHours_AreNeverQuiet()
    {
        var quietHours = QuietHours.Disabled;

        Assert.False(quietHours.IsEnabled);
        Assert.False(quietHours.IsQuiet(new TimeOnly(3, 0)));
    }

    [Fact]
    public void TryParse_AcceptsHhMm()
    {
        var parsed = QuietHours.TryParse("22:30", "08:00", out var quietHours);

        Assert.True(parsed);
        Assert.Equal(new TimeOnly(22, 30), quietHours.Start);
        Assert.Equal(new TimeOnly(8, 0), quietHours.End);
    }

    [Theory]
    [InlineData("22:30", "oops")]
    [InlineData(null, "08:00")]
    [InlineData("", "")]
    public void TryParse_RejectsInvalidValues(string? start, string? end)
    {
        Assert.False(QuietHours.TryParse(start, end, out _));
    }
}
