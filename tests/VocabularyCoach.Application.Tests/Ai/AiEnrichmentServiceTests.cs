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
        FakeEnrichmentProvider provider) =>
        new(
            new FakeSettingsService { Current = settings },
            cache,
            secrets,
            [provider],
            NullLogger<AiEnrichmentService>.Instance);
}
