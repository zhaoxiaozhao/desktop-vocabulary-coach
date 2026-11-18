using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Tests.Support;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Tests.Ai;

public class AiEnrichmentServiceTests
{
    private static readonly WordEnrichment SampleEnrichment = new("A.", "甲。", "简释", null);

    [Fact]
    public async Task GetCached_ReturnsCachedValue_WhenEnabled()
    {
        var cache = new FakeAiCacheStore { Cached = SampleEnrichment };
        var service = Create(cache, new FakeSecretStore(), EnabledSettings(AiPreset.Mock), new FakeEnrichmentProvider("Mock"));

        var result = await service.GetCachedAsync(1, CancellationToken.None);

        Assert.Same(SampleEnrichment, result);
    }

    [Fact]
    public async Task GetCached_ReturnsNull_WhenAiDisabled()
    {
        var cache = new FakeAiCacheStore { Cached = SampleEnrichment };
        var settings = new UserSettings { AiEnabled = false };
        var service = Create(cache, new FakeSecretStore(), settings, new FakeEnrichmentProvider("Mock"));

        Assert.Null(await service.GetCachedAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task EnrichNow_UsesProvider_AndCachesResult()
    {
        var cache = new FakeAiCacheStore();
        var provider = new FakeEnrichmentProvider("Mock") { Result = SampleEnrichment };
        var service = Create(cache, new FakeSecretStore(), EnabledSettings(AiPreset.Mock), provider);

        var card = new WordCard
        {
            Id = 7,
            Word = "resilient",
            Definitions = [new WordDefinition { DefinitionZh = "有韧性的" }],
        };

        var result = await service.EnrichNowAsync(card, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, cache.SaveCalls);
    }

    [Fact]
    public async Task EnrichNow_ReturnsNull_WhenProviderFails()
    {
        var cache = new FakeAiCacheStore();
        var provider = new FakeEnrichmentProvider("Mock") { Exception = new InvalidOperationException("boom") };
        var service = Create(cache, new FakeSecretStore(), EnabledSettings(AiPreset.Mock), provider);

        var card = new WordCard { Id = 7, Word = "resilient" };

        Assert.Null(await service.EnrichNowAsync(card, CancellationToken.None));
        Assert.Equal(0, cache.SaveCalls);
    }

    [Fact]
    public async Task TestAsync_FailsWithoutKey_ForNetworkProvider()
    {
        var provider = new FakeEnrichmentProvider("OpenAICompatible") { Result = SampleEnrichment };
        var service = Create(new FakeAiCacheStore(), new FakeSecretStore(), EnabledSettings(AiPreset.OpenAi), provider);

        var result = await service.TestAsync(null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, provider.CallCount);
        Assert.Contains("Key", result.Message);
    }

    [Fact]
    public async Task TestAsync_SucceedsWithStoredKey()
    {
        var secrets = new FakeSecretStore { Secret = "sk-test" };
        var provider = new FakeEnrichmentProvider("OpenAICompatible") { Result = SampleEnrichment };
        var service = Create(new FakeAiCacheStore(), secrets, EnabledSettings(AiPreset.OpenAi), provider);

        var result = await service.TestAsync(null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, provider.CallCount);
        Assert.DoesNotContain("sk-test", result.Message);
    }

    [Fact]
    public async Task TestAsync_MockProviderSucceedsOffline()
    {
        var service = Create(
            new FakeAiCacheStore(),
            new FakeSecretStore(),
            EnabledSettings(AiPreset.Mock),
            new FakeEnrichmentProvider("Mock"));

        var result = await service.TestAsync(null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Mock", result.Message);
    }

    [Fact]
    public async Task EnrichNow_AutoMode_FallsBackToNextConnection()
    {
        var settings = new UserSettings
        {
            AiEnabled = true,
            AiAutoMode = true,
            ActiveAiConnectionId = "a",
            AiConnections =
            [
                new AiConnection
                {
                    Id = "a",
                    Name = "A",
                    Preset = AiPreset.OpenAi,
                    BaseUrl = "https://fail.test/v1",
                    Model = "fail-model",
                },
                new AiConnection
                {
                    Id = "b",
                    Name = "B",
                    Preset = AiPreset.DeepSeek,
                    BaseUrl = "https://ok.test/v1",
                    Model = "ok-model",
                },
            ],
        };

        var keys = new FakeAiKeyStore();
        await keys.SetAsync("a", "sk-a");
        await keys.SetAsync("b", "sk-b");

        var provider = new FakeEnrichmentProvider("OpenAICompatible")
        {
            Handler = request => request.BaseUrl.Contains("ok.test")
                ? SampleEnrichment
                : null,
        };

        var cache = new FakeAiCacheStore();
        var service = new AiEnrichmentService(
            new FakeSettingsService { Current = settings },
            cache,
            keys,
            new AiConnectionResolver(keys),
            [provider],
            NullLogger<AiEnrichmentService>.Instance);

        var card = new WordCard { Word = "resilient" };

        var result = await service.EnrichNowAsync(card, CancellationToken.None);

        Assert.Same(SampleEnrichment, result);
        Assert.Equal(2, provider.CallCount);
    }

    private static UserSettings EnabledSettings(AiPreset preset) => new()
    {
        AiEnabled = true,
        AiPreset = preset,
        AiBaseUrl = "https://example.test/v1",
        AiModel = "test-model",
    };

    private static AiEnrichmentService Create(
        FakeAiCacheStore cache,
        FakeSecretStore secrets,
        UserSettings settings,
        FakeEnrichmentProvider provider)
    {
        var keyStore = new FakeAiKeyStore { Key = secrets.Secret };
        var settingsService = new FakeSettingsService { Current = settings };
        return new(
            settingsService,
            cache,
            keyStore,
            new AiConnectionResolver(keyStore),
            [provider],
            NullLogger<AiEnrichmentService>.Instance);
    }

    private sealed class FakeAiKeyStore : IAiKeyStore
    {
        public string? Key { get; set; }

        public Task<string?> GetAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Key);

        public Task SetAsync(
            string connectionId,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            Key = apiKey;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            Key = null;
            return Task.CompletedTask;
        }
    }
}
