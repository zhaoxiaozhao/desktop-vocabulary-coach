using VocabularyCoach.Application.Scheduling;

namespace VocabularyCoach.Application.Tests.Scheduling;

public class SystemStateTests
{
    [Fact]
    public void OkState_IsEligible()
    {
        Assert.True(SystemState.Ok().IsEligibleForPopup);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void AnyBlockingCondition_MakesStateIneligible(
        bool isIdle,
        bool isLocked,
        bool isFullscreen,
        bool detectionFailed)
    {
        var state = new SystemState
        {
            IsIdle = isIdle,
            IsSessionLocked = isLocked,
            IsFullscreenAppActive = isFullscreen,
            DetectionFailed = detectionFailed,
        };

        Assert.False(state.IsEligibleForPopup);
        Assert.False(string.IsNullOrWhiteSpace(state.Describe()));
    }

    [Fact]
    public void Describe_IncludesIdleMinutes()
    {
        var state = new SystemState { IsIdle = true, IdleTime = TimeSpan.FromMinutes(7.4) };

        Assert.Contains("7.4", state.Describe());
    }
}
