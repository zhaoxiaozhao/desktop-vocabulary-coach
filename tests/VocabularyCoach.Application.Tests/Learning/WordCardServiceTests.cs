using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Tests.Learning;

public class WordCardServiceTests
{
    private static readonly WordCard SampleCard = new()
    {
        Word = "apple",
        Phonetic = "ˈæpl",
        Definitions = [new WordDefinition { DefinitionZh = "苹果" }],
    };

    [Fact]
    public async Task ShowNextCardAsync_ForwardsPresenterResult()
    {
        var source = new FakeWordCardSource(SampleCard);
        var presenter = new FakePopupPresenter(async (_, _) =>
        {
            await Task.Yield();
            return WordCardResult.Recognized;
        });
        var service = CreateService(source, presenter);

        var result = await service.ShowNextCardAsync();

        Assert.Equal(WordCardResult.Recognized, result);
        Assert.Equal(1, presenter.ShowCallCount);
        Assert.Same(SampleCard, presenter.LastCard);
    }

    [Fact]
    public async Task ShowNextCardAsync_ReturnsNullWithoutInvokingPresenter_WhenNoWordsAvailable()
    {
        var source = new FakeWordCardSource(null);
        var presenter = new FakePopupPresenter((_, _) => Task.FromResult(WordCardResult.Dismissed));
        var service = CreateService(source, presenter);

        var result = await service.ShowNextCardAsync();

        Assert.Null(result);
        Assert.Equal(0, presenter.ShowCallCount);
    }

    [Fact]
    public async Task ShowNextCardAsync_RejectsConcurrentCards()
    {
        var firstCardShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCard = new TaskCompletionSource<WordCardResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = new FakeWordCardSource(SampleCard);
        var presenter = new FakePopupPresenter(async (_, _) =>
        {
            firstCardShown.TrySetResult();
            return await releaseFirstCard.Task;
        });
        var service = CreateService(source, presenter);

        var firstShow = service.ShowNextCardAsync();
        await firstCardShown.Task;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ShowNextCardAsync());

        Assert.Contains("正在显示", exception.Message);

        releaseFirstCard.SetResult(WordCardResult.Vague);
        Assert.Equal(WordCardResult.Vague, await firstShow);
    }

    [Fact]
    public async Task ShowNextCardAsync_ReleasesGate_AfterPresenterFailure()
    {
        var attempts = 0;
        var source = new FakeWordCardSource(SampleCard);
        var presenter = new FakePopupPresenter((_, _) =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<WordCardResult>(new InvalidOperationException("boom"))
                : Task.FromResult(WordCardResult.Unknown);
        });
        var service = CreateService(source, presenter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ShowNextCardAsync());
        var secondResult = await service.ShowNextCardAsync();

        Assert.Equal(WordCardResult.Unknown, secondResult);
    }

    [Fact]
    public async Task ShowNextCardAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var presenter = new FakePopupPresenter(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return WordCardResult.Dismissed;
        });
        var service = CreateService(new FakeWordCardSource(SampleCard), presenter);

        var showTask = service.ShowNextCardAsync(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showTask);
    }

    [Fact]
    public async Task ShowNextCardAsync_PersistsReview_ForMasteryFeedback()
    {
        var store = new RecordingLearningStore();
        var service = CreateService(
            new FakeWordCardSource(CardWithId(42)),
            new FakePopupPresenter((_, _) => Task.FromResult(WordCardResult.Recognized)),
            store);

        await service.ShowNextCardAsync(LearningSource.Manual);

        var outcome = Assert.Single(store.SavedOutcomes);
        Assert.Equal(42, outcome.WordId);
        Assert.Equal(WordCardResult.Recognized, outcome.Result);
        Assert.Equal(WordFeedback.Recognized, outcome.Feedback);
        Assert.Equal(LearningSource.Manual, outcome.Source);
        Assert.NotNull(outcome.Schedule);
        Assert.Equal(1, outcome.Schedule!.Repetition);
        Assert.Equal(1, outcome.Schedule.IntervalDays);
        Assert.Equal(ReviewRating.Good, outcome.Schedule.Rating);
        Assert.True(outcome.DurationMs >= 0);
    }

    [Fact]
    public async Task ShowNextCardAsync_PersistsOnlySession_ForDismissedCard()
    {
        var store = new RecordingLearningStore();
        var service = CreateService(
            new FakeWordCardSource(CardWithId(42)),
            new FakePopupPresenter((_, _) => Task.FromResult(WordCardResult.Dismissed)),
            store);

        await service.ShowNextCardAsync(LearningSource.Popup);

        var outcome = Assert.Single(store.SavedOutcomes);
        Assert.Equal(WordCardResult.Dismissed, outcome.Result);
        Assert.Null(outcome.Feedback);
        Assert.Null(outcome.Schedule);
        Assert.Equal(LearningSource.Popup, outcome.Source);
    }

    [Fact]
    public async Task ShowNextCardAsync_DoesNotPersist_WhenCardHasNoDatabaseId()
    {
        var store = new RecordingLearningStore();
        var service = CreateService(
            new FakeWordCardSource(SampleCard),
            new FakePopupPresenter((_, _) => Task.FromResult(WordCardResult.Recognized)),
            store);

        await service.ShowNextCardAsync();

        Assert.Empty(store.SavedOutcomes);
    }

    [Fact]
    public async Task ShowNextCardAsync_UsesCachedAiEnrichment()
    {
        var enrichment = new VocabularyCoach.Core.Entities.WordEnrichment("AI example.", "AI 例句。", "简释", null);
        var ai = new FakeAiEnrichmentService { Cached = enrichment };
        var presenter = new FakePopupPresenter((_, _) => Task.FromResult(WordCardResult.Dismissed));
        var service = CreateService(new FakeWordCardSource(CardWithId(42)), presenter, ai: ai);

        await service.ShowNextCardAsync();

        Assert.NotNull(presenter.LastCard);
        Assert.Same(enrichment, presenter.LastCard!.Enrichment);
    }

    [Fact]
    public async Task ShowNextCardAsync_QueuesAiEnrichment_WhenCacheIsEmpty()
    {
        var ai = new FakeAiEnrichmentService();
        var service = CreateService(
            new FakeWordCardSource(CardWithId(42)),
            new FakePopupPresenter((_, _) => Task.FromResult(WordCardResult.Dismissed)),
            ai: ai);

        await service.ShowNextCardAsync();

        var queued = Assert.Single(ai.Queued);
        Assert.Equal(42, queued.Id);
        Assert.Null(queued.Enrichment);
    }

    [Fact]
    public async Task ShowNextCard_WhenLicenseExpired_ReturnsNullWithoutPresenter()
    {
        var license = new FakeLicenseService();
        license.Expire();

        var presenter = new FakePopupPresenter(
            (_, _) => Task.FromResult(WordCardResult.Recognized));

        var service = CreateService(
            new FakeWordCardSource(CardWithId(42)),
            presenter,
            license: license);

        var result = await service.ShowNextCardAsync();

        Assert.Null(result);
        Assert.Equal(0, presenter.ShowCallCount);
    }

    private static WordCardService CreateService(
        IWordCardSource source,
        IWordPopupPresenter presenter,
        RecordingLearningStore? store = null,
        FakeAiEnrichmentService? ai = null,
        FakeLicenseService? license = null) =>
        new(
            source,
            presenter,
            store ?? new RecordingLearningStore(),
            new Sm2ReviewScheduler(),
            new FakeAdaptiveDifficultyService(),
            ai ?? new FakeAiEnrichmentService(),
            license ?? new FakeLicenseService(),
            TimeProvider.System,
            NullLogger<WordCardService>.Instance);

    private static WordCard CardWithId(int id) => new()
    {
        Id = id,
        Word = "apple",
        Definitions = [new WordDefinition { DefinitionZh = "苹果" }],
    };

    private sealed class FakeWordCardSource : IWordCardSource
    {
        private readonly WordCard? _card;

        public FakeWordCardSource(WordCard? card) => _card = card;

        public Task<WordCard?> GetNextCardAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_card);
    }

    private sealed class FakePopupPresenter : IWordPopupPresenter
    {
        private readonly Func<WordCard, CancellationToken, Task<WordCardResult>> _handler;

        public FakePopupPresenter(Func<WordCard, CancellationToken, Task<WordCardResult>> handler) =>
            _handler = handler;

        public int ShowCallCount { get; private set; }

        public WordCard? LastCard { get; private set; }

        public Task<WordCardResult> ShowAsync(WordCard card, CancellationToken cancellationToken)
        {
            ShowCallCount++;
            LastCard = card;
            return _handler(card, cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
