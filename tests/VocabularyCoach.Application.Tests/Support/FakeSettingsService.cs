using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class FakeSettingsService : ISettingsService
{
    public UserSettings Current { get; set; } = new();

    public SchedulerOptions Scheduler { get; set; } = new();

    public RecommendationOptions Recommendation { get; set; } = new();

    public CardDisplayOptions Card { get; set; } = new();

    public QuietHours QuietHours { get; set; } = QuietHours.Disabled;

    public bool IsInitialized { get; private set; } = true;

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MarkInitializedAsync(CancellationToken cancellationToken)
    {
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAdaptiveDifficultyService : VocabularyCoach.Application.Adaptive.IAdaptiveDifficultyService
{
    public int ApplyCount { get; private set; }

    public Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        ApplyCount++;
        return Task.FromResult(2);
    }

    public Task ResetPreferenceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
