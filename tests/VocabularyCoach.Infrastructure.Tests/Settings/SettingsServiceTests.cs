using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Infrastructure.Repositories;
using VocabularyCoach.Infrastructure.Tests.Support;

namespace VocabularyCoach.Infrastructure.Tests.Settings;

public class SettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsAllSettings()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var store = new EfSettingsStore(database.Factory);
        var first = CreateService(store);

        await first.LoadAsync(CancellationToken.None);
        await first.SaveAsync(
            new UserSettings
            {
                RemindersEnabled = false,
                MinIntervalMinutes = 5,
                MaxIntervalMinutes = 20,
                MaxPopupsPerHour = 6,
                QuietHoursStart = "23:00",
                QuietHoursEnd = "07:30",
                IdleThresholdMinutes = 5,
                EnableIdleDetection = false,
                DetectFullscreen = false,
                PauseUntilTomorrowTime = "08:00",
                RecommendationMode = RecommendationMode.ReviewOnly,
                DifficultyMode = DifficultyMode.Manual,
                InteractionMode = InteractionMode.ChoiceQuiz,
                ManualDifficulty = 4,
                NewWordRatio = 0.35,
                Theme = CardTheme.Dark,
                ShowPhonetic = false,
                AutoExpand = true,
                Animate = false,
                AiEnabled = true,
                AiPreset = AiPreset.DeepSeek,
                AiBaseUrl = "https://api.deepseek.com/v1",
                AiModel = "deepseek-chat",
                CaptureEnabled = false,
                CaptureSilentAdd = true,
                CaptureShowNotification = false,
                StartWithWindows = true,
            },
            CancellationToken.None);

        var second = CreateService(store);
        await second.LoadAsync(CancellationToken.None);

        Assert.False(second.Current.RemindersEnabled);
        Assert.Equal(5, second.Current.MinIntervalMinutes);
        Assert.Equal(20, second.Current.MaxIntervalMinutes);
        Assert.Equal(6, second.Current.MaxPopupsPerHour);
        Assert.Equal("23:00", second.Current.QuietHoursStart);
        Assert.Equal("08:00", second.Current.PauseUntilTomorrowTime);
        Assert.Equal(RecommendationMode.ReviewOnly, second.Current.RecommendationMode);
        Assert.Equal(DifficultyMode.Manual, second.Current.DifficultyMode);
        Assert.Equal(InteractionMode.ChoiceQuiz, second.Current.InteractionMode);
        Assert.Equal(4, second.Current.ManualDifficulty);
        Assert.Equal(0.35, second.Current.NewWordRatio, 3);
        Assert.Equal(CardTheme.Dark, second.Current.Theme);
        Assert.False(second.Current.ShowPhonetic);
        Assert.True(second.Current.AutoExpand);
        Assert.False(second.Current.Animate);
        Assert.True(second.Current.AiEnabled);
        Assert.Equal(AiPreset.DeepSeek, second.Current.AiPreset);
        Assert.Equal("https://api.deepseek.com/v1", second.Current.AiBaseUrl);
        Assert.Equal("deepseek-chat", second.Current.AiModel);
        Assert.False(second.Current.CaptureEnabled);
        Assert.True(second.Current.CaptureSilentAdd);
        Assert.False(second.Current.CaptureShowNotification);
        Assert.True(second.Current.StartWithWindows);

        // 派生配置同步更新
        Assert.Equal(6, second.Scheduler.MaxPopupsPerHour);
        Assert.False(second.Scheduler.Enabled);
        Assert.Equal(0.35, second.Recommendation.NewWordRatio, 3);
        Assert.Equal(CardTheme.Dark, second.Card.Theme);
    }

    [Fact]
    public async Task LoadWithoutSavedSettings_UsesAppSettingsDefaults()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var service = CreateService(new EfSettingsStore(database.Factory));
        await service.LoadAsync(CancellationToken.None);

        Assert.True(service.Current.RemindersEnabled);
        Assert.Equal(12, service.Current.MinIntervalMinutes);
        Assert.Equal(35, service.Current.MaxIntervalMinutes);
        Assert.Equal(CardTheme.Light, service.Current.Theme);
    }

    [Fact]
    public async Task Save_RejectsInvalidQuietHours()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var service = CreateService(new EfSettingsStore(database.Factory));
        await service.LoadAsync(CancellationToken.None);

        var invalid = service.Current.Clone();
        invalid.QuietHoursStart = "25:99";

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(invalid, CancellationToken.None));
    }

    [Fact]
    public async Task Save_RejectsInvalidPauseUntilTomorrowTime()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var service = CreateService(new EfSettingsStore(database.Factory));
        await service.LoadAsync(CancellationToken.None);

        var invalid = service.Current.Clone();
        invalid.PauseUntilTomorrowTime = "25:99";

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync(invalid, CancellationToken.None));
    }

    [Fact]
    public async Task Save_NormalizesOutOfRangeValues()
    {
        await using var database = new SqliteTestDatabase();
        await database.InitializeAsync();

        var service = CreateService(new EfSettingsStore(database.Factory));
        await service.LoadAsync(CancellationToken.None);

        var settings = service.Current.Clone();
        settings.MinIntervalMinutes = 30;
        settings.MaxIntervalMinutes = 10;
        settings.MaxPopupsPerHour = 0;
        settings.NewWordRatio = 5;

        await service.SaveAsync(settings, CancellationToken.None);

        Assert.Equal(30, service.Current.MinIntervalMinutes);
        Assert.Equal(30, service.Current.MaxIntervalMinutes);
        Assert.Equal(1, service.Current.MaxPopupsPerHour);
        Assert.Equal(1, service.Current.NewWordRatio, 3);
    }

    private static SettingsService CreateService(ISettingsStore store) =>
        new(
            store,
            Options.Create(new SchedulerOptions()),
            Options.Create(new RecommendationOptions()),
            Options.Create(new CardDisplayOptions()),
            NullLogger<SettingsService>.Instance);
}
