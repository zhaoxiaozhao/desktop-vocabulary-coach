using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Application.SentencePractice;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Tests.SentencePractice;

public class SentencePracticeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateSession_UsesOfflineBank()
    {
        var service = CreateService();

        var session = await service.CreateSessionAsync(10);
        var items = session.Items;

        Assert.False(session.AiEnabled);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.False(item.FromAi));
        Assert.All(items, item => Assert.Equal(4, item.Options.Count));
        Assert.Contains("favor", items.Select(x => x.Word));
        Assert.Contains("afford", items.Select(x => x.Word));
    }

    [Fact]
    public async Task CreateSession_PrefersAiItems_AndFallsBackPerWord()
    {
        var generator = new FakeSentenceGenerator
        {
            Items =
            [
                new SentencePracticeItem(
                    1,
                    "favor",
                    "Could you do me a ____, please?",
                    "favor",
                    [],
                    "你能帮我个忙吗？",
                    "do sb a favor",
                    "AI 句式",
                    ["favor", "case", "matter", "problem"],
                    FromAi: true),
            ],
        };
        var service = CreateService(aiEnabled: true, generator: generator);

        var session = await service.CreateSessionAsync(10);
        var items = session.Items;

        Assert.True(session.AiEnabled);
        Assert.True(session.AiUsed);
        Assert.Equal(2, items.Count);
        var favor = items.Single(x => x.Word == "favor");
        Assert.True(favor.FromAi);
        Assert.Equal("AI 句式", favor.GrammarNote);

        var afford = items.Single(x => x.Word == "afford");
        Assert.False(afford.FromAi);
    }

    [Theory]
    [InlineData("favor", true)]
    [InlineData(" FAVOR. ", true)]
    [InlineData("case", false)]
    [InlineData("", false)]
    public async Task Submit_ChecksAnswerAndRecordsOutcome(string answer, bool expected)
    {
        var store = new RecordingLearningStore();
        var service = CreateService(store: store);
        var item = (await service.CreateSessionAsync(10)).Items.Single(x => x.Word == "favor");

        var outcome = await service.SubmitAsync(item, answer, Now.AddSeconds(-5));

        Assert.Equal(expected, outcome.IsCorrect);
        var saved = Assert.Single(store.SavedOutcomes);
        Assert.Equal(LearningSource.SentencePractice, saved.Source);
        Assert.Equal(expected ? WordCardResult.Recognized : WordCardResult.Unknown, saved.Result);
        Assert.Equal(5000, saved.DurationMs);
    }

    [Fact]
    public async Task Submit_HighFrequencyWordOutsideLibrary_StillPracticesButDoesNotRecord()
    {
        var store = new RecordingLearningStore();
        var service = CreateService(store: store, bank: new GoSentenceBank());
        var item = Assert.Single((await service.CreateSessionAsync(10)).Items);

        Assert.Equal(0, item.WordId);
        var outcome = await service.SubmitAsync(item, "went", Now);

        Assert.True(outcome.IsCorrect);
        Assert.Empty(store.SavedOutcomes);
    }

    [Fact]
    public async Task CreateSession_WhenAiFails_UsesCooldownBeforeRetrying()
    {
        var clock = new FakeTimeProvider(Now);
        var generator = new FakeSentenceGenerator { Items = [] };
        var service = CreateService(aiEnabled: true, generator: generator, timeProvider: clock);

        var first = await service.CreateSessionAsync(10);

        Assert.False(first.AiUsed);
        Assert.Equal(1, generator.CallCount);
        Assert.Contains("离线", first.AiMessage);

        var second = await service.CreateSessionAsync(10);

        Assert.False(second.AiUsed);
        Assert.Equal(1, generator.CallCount);
        Assert.Contains("离线", second.AiMessage);

        clock.Advance(TimeSpan.FromMinutes(11));
        await service.CreateSessionAsync(10);

        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task CreateSession_ReasoningModel_SkipsBatchGeneration()
    {
        var generator = new FakeSentenceGenerator { Items = [] };
        var service = CreateService(
            aiEnabled: true,
            generator: generator,
            aiModel: "glm-5.3-flash");

        var session = await service.CreateSessionAsync(10);

        Assert.False(session.AiUsed);
        Assert.Equal(0, generator.CallCount);
        Assert.Contains("推理模型", session.AiMessage);
        Assert.Equal(2, session.Items.Count);
    }

    [Fact]
    public async Task CreateSession_SentenceModelOverride_UsesFastModel()
    {
        var generator = new FakeSentenceGenerator
        {
            Items =
            [
                new SentencePracticeItem(
                    1,
                    "favor",
                    "Could you do me a ____?",
                    "favor",
                    [],
                    "你能帮我个忙吗？",
                    "do sb a favor",
                    "favor 作名词。",
                    ["favor", "case", "matter", "problem"],
                    FromAi: true),
            ],
        };
        var service = CreateService(
            aiEnabled: true,
            generator: generator,
            aiModel: "glm-5.3-flash",
            sentencePracticeModel: "glm-4-flash");

        var session = await service.CreateSessionAsync(10);

        Assert.True(session.AiUsed);
        Assert.Equal(1, generator.CallCount);
        Assert.Empty(session.AiMessage ?? string.Empty);
    }

    private static SentencePracticeService CreateService(
        RecordingLearningStore? store = null,
        bool aiEnabled = false,
        FakeSentenceGenerator? generator = null,
        ISentenceBank? bank = null,
        FakeTimeProvider? timeProvider = null,
        string aiModel = "glm-4-flash",
        string sentencePracticeModel = "")
    {
        var settings = new FakeSettingsService
        {
            Current = new VocabularyCoach.Application.Settings.UserSettings
            {
                AiEnabled = aiEnabled,
                AiModel = aiModel,
                SentencePracticeModel = sentencePracticeModel,
            },
        };

        return new SentencePracticeService(
            new StubWordLibrary(),
            bank ?? new StubSentenceBank(),
            generator ?? new FakeSentenceGenerator(),
            settings,
            store ?? new RecordingLearningStore(),
            new Sm2ReviewScheduler(),
            timeProvider ?? new FakeTimeProvider(Now),
            NullLogger<SentencePracticeService>.Instance);
    }

    private sealed class StubWordLibrary : IWordLibrary
    {
        private static readonly WordCard Favor = Card(1, "favor", "帮忙");
        private static readonly WordCard Afford = Card(2, "afford", "负担得起");

        public Task<WordCard?> FindByWordAsync(string word, CancellationToken cancellationToken = default) =>
            Task.FromResult<WordCard?>(word.ToLowerInvariant() switch
            {
                "favor" => Favor,
                "afford" => Afford,
                _ => null,
            });

        public Task<WordCard?> GetNextNewWordAsync(int maxDifficulty, CancellationToken cancellationToken = default) =>
            Task.FromResult<WordCard?>(null);

        public Task<int> GetWordCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(2);

        public Task<IReadOnlyList<string>> GetDistractorDefinitionsAsync(
            int excludeWordId,
            int count,
            int preferredDifficulty,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetDistractorWordsAsync(
            int excludeWordId,
            string correctDefinition,
            int preferredLength,
            int count,
            int preferredDifficulty,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        private static WordCard Card(int id, string word, string definition) => new()
        {
            Id = id,
            Word = word,
            Definitions = [new WordDefinition { DefinitionZh = definition }],
        };
    }

    private sealed class StubSentenceBank : ISentenceBank
    {
        public IReadOnlyList<SentenceSeedEntry> Entries { get; } =
        [
            new(
                "favor",
                "Could you do me a ____?",
                "favor",
                "你能帮我个忙吗？",
                "do sb a favor",
                "favor 作名词。",
                ["case", "matter", "problem"]),
            new(
                "afford",
                "I can't ____ to buy a new car.",
                "afford",
                "我买不起新车。",
                "afford to do",
                "afford 后接 to do。",
                ["spend", "pay", "cost"]),
        ];
    }

    private sealed class GoSentenceBank : ISentenceBank
    {
        public IReadOnlyList<SentenceSeedEntry> Entries { get; } =
        [
            new(
                "go",
                "I ____ to the gym yesterday.",
                "went",
                "我昨天去健身房了。",
                "go 的过去式",
                "yesterday 表示过去，go 的过去式是 went。",
                ["go", "gone", "going"]),
        ];
    }

    private sealed class FakeSentenceGenerator : ISentencePracticeGenerator
    {
        public IReadOnlyList<SentencePracticeItem> Items { get; set; } = [];

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<SentencePracticeItem>> GenerateAsync(
            IReadOnlyList<WordCard> cards,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Items);
        }
    }
}
