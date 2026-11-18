using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Infrastructure.Ai;

namespace VocabularyCoach.Infrastructure.Tests.Ai;

public class DpapiAiKeyStoreTests
{
    [Fact]
    public async Task StoresKeysPerConnection_AndMigratesLegacyDefault()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"dvc-ai-keys-{Guid.NewGuid():N}");
        var legacyPath = Path.Combine(directory, "ai.key");

        try
        {
            Directory.CreateDirectory(directory);
            var legacy = new DpapiSecretStore(
                legacyPath,
                NullLogger<DpapiSecretStore>.Instance);
            await legacy.SaveAsync("legacy-default-key", CancellationToken.None);

            var store = new DpapiAiKeyStore(
                directory,
                legacyPath,
                NullLogger<DpapiAiKeyStore>.Instance);

            Assert.Equal("legacy-default-key", await store.GetAsync("default"));
            Assert.True(File.Exists(Path.Combine(directory, "ai.default.key")));

            await store.SetAsync("conn-b", "second-key", CancellationToken.None);
            Assert.Equal("second-key", await store.GetAsync("conn-b"));

            await store.DeleteAsync("conn-b", CancellationToken.None);
            Assert.Null(await store.GetAsync("conn-b"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
