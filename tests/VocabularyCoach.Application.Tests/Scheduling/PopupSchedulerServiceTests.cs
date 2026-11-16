using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Application.Tests.Support;

namespace VocabularyCoach.Application.Tests.Scheduling;

public class PopupSchedulerServiceTests
{
    [Fact]
    public async Task StartAsync_KeepsRunning_UntilStopIsRequested()
    {
        var harness = CreateHarness(FastOptions());
        await harness.Service.StartAsync(CancellationToken.None);

        Assert.NotNull(harness.Service.ExecuteTask);
        Assert.False(harness.Service.ExecuteTask!.IsCompleted);
        Assert.True(harness.Service.IsRunning);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync(timeout.Token);

        Assert.True(harness.Service.ExecuteTask!.IsCompleted);
        Assert.False(harness.Service.IsRunning);
    }

    [Fact]
    public async Task StopAsync_CompletesQuickly()
    {
        var harness = CreateHarness(FastOptions());
        await harness.Service.StartAsync(CancellationToken.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await harness.Service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"停止耗时 {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ShowsCard_WhenSystemStateIsEligible()
    {
        var harness = CreateHarness(FastOptions());

        try
        {
            await harness.Service.StartAsync(CancellationToken.None);

            Assert.True(
                await WaitUntilAsync(() => harness.Presenter.ShowCount > 0, TimeSpan.FromSeconds(5)),
                "调度器未在预期时间内弹卡。");
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotShowCard_WhenSessionIsLocked()
    {
        var harness = CreateHarness(FastOptions(), new SystemState { IsSessionLocked = true });

        try
        {
            await harness.Service.StartAsync(CancellationToken.None);
            await Task.Delay(700);

            Assert.Equal(0, harness.Presenter.ShowCount);
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotShowCard_DuringQuietHours()
    {
        var now = DateTimeOffset.Now;
        var options = FastOptions();
        options.QuietHoursStart = now.AddHours(-1).ToString("HH:mm");
        options.QuietHoursEnd = now.AddHours(1).ToString("HH:mm");

        var harness = CreateHarness(options);

        try
        {
            await harness.Service.StartAsync(CancellationToken.None);
            await Task.Delay(700);

            Assert.Equal(0, harness.Presenter.ShowCount);
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotShowCard_WhilePaused_ThenShowsAfterResume()
    {
        var pauseState = new PauseState(TimeProvider.System);
        pauseState.Pause(TimeSpan.FromHours(1));

        var harness = CreateHarness(FastOptions(), SystemState.Ok(), pauseState);

        try
        {
            await harness.Service.StartAsync(CancellationToken.None);
            await Task.Delay(500);
            Assert.Equal(0, harness.Presenter.ShowCount);

            pauseState.Resume();

            Assert.True(
                await WaitUntilAsync(() => harness.Presenter.ShowCount > 0, TimeSpan.FromSeconds(5)),
                "恢复提醒后未重新弹卡。");
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotExceedHourlyLimit()
    {
        var harness = CreateHarness(FastOptions(maxPopupsPerHour: 1));

        try
        {
            await harness.Service.StartAsync(CancellationToken.None);

            Assert.True(
                await WaitUntilAsync(() => harness.Presenter.ShowCount >= 1, TimeSpan.FromSeconds(5)),
                "调度器未弹出第一张卡片。");

            await Task.Delay(700);
            Assert.Equal(1, harness.Presenter.ShowCount);
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SettingsChange_RestartsWaitWithNewInterval()
    {
        var slow = FastOptions();
        slow.MinIntervalMinutes = 10;
        slow.MaxIntervalMinutes = 10;

        var harness = CreateHarness(slow);

        try
        {
            await harness.Service.StartAsync(CancellationToken.None);
            await Task.Delay(300);
            Assert.Equal(0, harness.Presenter.ShowCount);

            // 模拟用户在设置界面把间隔改短并保存。
            harness.Settings.Scheduler = FastOptions();
            harness.Settings.RaiseChanged();

            Assert.True(
                await WaitUntilAsync(() => harness.Presenter.ShowCount > 0, TimeSpan.FromSeconds(5)),
                "设置变更后未按新间隔重新调度。");
        }
        finally
        {
            await harness.Service.StopAsync(CancellationToken.None);
        }
    }

    private static SchedulerOptions FastOptions(int maxPopupsPerHour = 100) => new()
    {
        // 0.002 分钟 = 120ms，仅用于测试，生产默认为 12-35 分钟。
        MinIntervalMinutes = 0.002,
        MaxIntervalMinutes = 0.003,
        RecheckDelayMinutes = 0.002,
        MaxPopupsPerHour = maxPopupsPerHour,
        QuietHoursStart = "00:00",
        QuietHoursEnd = "00:00",
        EnableIdleDetection = false,
        DetectFullscreen = false,
    };

    private static SchedulerHarness CreateHarness(
        SchedulerOptions options,
        SystemState? state = null,
        PauseState? pauseState = null)
    {
        var presenter = new CountingPresenter();
        var wordCardService = new WordCardService(
            new StaticWordCardSource(),
            presenter,
            new RecordingLearningStore(),
            new Sm2ReviewScheduler(),
            new FakeAdaptiveDifficultyService(),
            new FakeAiEnrichmentService(),
            TimeProvider.System,
            NullLogger<WordCardService>.Instance);

        var monitor = new FakeSystemStateMonitor { State = state ?? SystemState.Ok() };
        var settings = new FakeSettingsService
        {
            Scheduler = options,
            QuietHours = options.GetQuietHours(),
        };
        var rateLimiter = new PopupRateLimiter(settings, TimeProvider.System);
        var pause = pauseState ?? new PauseState(TimeProvider.System);

        var service = new PopupSchedulerService(
            settings,
            monitor,
            rateLimiter,
            pause,
            wordCardService,
            TimeProvider.System,
            NullLogger<PopupSchedulerService>.Instance);

        return new SchedulerHarness(service, presenter, settings);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private sealed record SchedulerHarness(
        PopupSchedulerService Service,
        CountingPresenter Presenter,
        FakeSettingsService Settings);

    private sealed class CountingPresenter : IWordPopupPresenter
    {
        private int _showCount;

        public int ShowCount => Volatile.Read(ref _showCount);

        public Task<WordCardResult> ShowAsync(WordCard card, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _showCount);
            return Task.FromResult(WordCardResult.Recognized);
        }

        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StaticWordCardSource : IWordCardSource
    {
        public Task<WordCard?> GetNextCardAsync(CancellationToken cancellationToken) =>
            Task.FromResult<WordCard?>(new WordCard
            {
                Word = "test",
                Phonetic = "test",
                Definitions = [new WordDefinition { DefinitionZh = "测试" }],
            });
    }

    private sealed class FakeSystemStateMonitor : ISystemStateMonitor
    {
        public SystemState State { get; set; } = SystemState.Ok();

        public Task<SystemState> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);
    }
}
