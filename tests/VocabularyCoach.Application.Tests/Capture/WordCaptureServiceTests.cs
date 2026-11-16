using VocabularyCoach.Application.Capture;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Capture;

public class WordCaptureServiceTests
{
    [Fact]
    public async Task Add_NewLibraryWord_CreatesDueProgress_AndCapture()
    {
        var library = new FakeWordLibrary
        {
            FoundWord = new WordCard
            {
                Id = 42,
                Word = "resilient",
                Definitions = [new WordDefinition { DefinitionZh = "有韧性的" }],
            },
        };
        var store = new FakeWordCaptureStore();
        var service = new WordCaptureService(library, store);

        var result = await service.AddAsync("Resilient", null, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Added, result.Outcome);
        Assert.Equal([42], store.DueNowWordIds);
        Assert.Single(store.Captures);
        Assert.Equal("resilient", store.Captures[0].NormalizedWord);
        Assert.Empty(store.CreatedWords);
    }

    [Fact]
    public async Task Add_ExistingLearningWord_ReportsAlreadyLearning()
    {
        var library = new FakeWordLibrary
        {
            FoundWord = new WordCard { Id = 7, Word = "apple" },
        };
        var store = new FakeWordCaptureStore { HasProgress = true };
        var service = new WordCaptureService(library, store);

        var result = await service.AddAsync("apple", null, CancellationToken.None);

        Assert.Equal(CaptureOutcome.AlreadyLearning, result.Outcome);
        Assert.Empty(store.DueNowWordIds);
        Assert.Single(store.Captures);
    }

    [Fact]
    public async Task Add_UnknownWordWithoutDefinition_RequiresDefinition()
    {
        var service = new WordCaptureService(new FakeWordLibrary(), new FakeWordCaptureStore());

        var result = await service.AddAsync("serendipity", null, CancellationToken.None);

        Assert.Equal(CaptureOutcome.NeedsDefinition, result.Outcome);
    }

    [Fact]
    public async Task Add_UnknownPhraseWithDefinition_CreatesCustomWord()
    {
        var store = new FakeWordCaptureStore();
        var service = new WordCaptureService(new FakeWordLibrary(), store);

        var result = await service.AddAsync("take off", "起飞；脱下", CancellationToken.None);

        Assert.Equal(CaptureOutcome.Added, result.Outcome);
        var created = Assert.Single(store.CreatedWords);
        Assert.Equal("take off", created.NormalizedWord);
        Assert.Equal("起飞；脱下", created.DefinitionZh);
        Assert.Single(store.Captures);
    }

    [Fact]
    public async Task Add_InvalidText_ReturnsInvalid()
    {
        var service = new WordCaptureService(new FakeWordLibrary(), new FakeWordCaptureStore());

        var result = await service.AddAsync("hello, world", null, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Inspect_ReturnsSuggestedDefinition_AndLearningState()
    {
        var library = new FakeWordLibrary
        {
            FoundWord = new WordCard
            {
                Id = 5,
                Word = "momentum",
                Definitions = [new WordDefinition { DefinitionZh = "势头" }],
            },
        };
        var store = new FakeWordCaptureStore { HasProgress = true };
        var service = new WordCaptureService(library, store);

        var preview = await service.InspectAsync("momentum", CancellationToken.None);

        Assert.True(preview.IsValid);
        Assert.True(preview.ExistsInLibrary);
        Assert.True(preview.AlreadyLearning);
        Assert.Equal("势头", preview.SuggestedDefinition);
    }

    [Fact]
    public async Task Inspect_InvalidText_ReturnsError()
    {
        var service = new WordCaptureService(new FakeWordLibrary(), new FakeWordCaptureStore());

        var preview = await service.InspectAsync("中文", CancellationToken.None);

        Assert.False(preview.IsValid);
        Assert.NotNull(preview.Error);
    }

    private sealed class FakeWordCaptureStore : IWordCaptureStore
    {
        public bool HasProgress { get; set; }

        public List<int> DueNowWordIds { get; } = [];

        public List<(string Word, string NormalizedWord, string DefinitionZh)> CreatedWords { get; } = [];

        public List<(string RawText, string NormalizedWord, int WordId)> Captures { get; } = [];

        public Task<bool> HasProgressAsync(int wordId, CancellationToken cancellationToken) =>
            Task.FromResult(HasProgress);

        public Task<int> CreateWordWithProgressAsync(
            string word,
            string normalizedWord,
            string definitionZh,
            CancellationToken cancellationToken)
        {
            CreatedWords.Add((word, normalizedWord, definitionZh));
            return Task.FromResult(100);
        }

        public Task EnsureDueNowAsync(int wordId, CancellationToken cancellationToken)
        {
            DueNowWordIds.Add(wordId);
            return Task.CompletedTask;
        }

        public Task RecordCaptureAsync(
            string rawText,
            string normalizedWord,
            int wordId,
            CancellationToken cancellationToken)
        {
            Captures.Add((rawText, normalizedWord, wordId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CapturedWord>> GetCapturedAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CapturedWord>>([]);
    }
}
