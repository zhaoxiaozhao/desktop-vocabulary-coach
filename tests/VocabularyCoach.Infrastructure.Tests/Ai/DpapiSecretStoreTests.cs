using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Infrastructure.Ai;

namespace VocabularyCoach.Infrastructure.Tests.Ai;

public class DpapiSecretStoreTests
{
    [Fact]
    public async Task SaveAndRead_RoundTripsSecret_WithoutPlaintextOnDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vc-secret-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ai.key");

        try
        {
            var store = new DpapiSecretStore(path, NullLogger<DpapiSecretStore>.Instance);

            await store.SaveAsync("sk-test-secret-value", CancellationToken.None);

            Assert.True(File.Exists(path));
            var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            Assert.DoesNotContain("sk-test-secret-value", raw);

            Assert.Equal("sk-test-secret-value", await store.TryReadAsync(CancellationToken.None));

            await store.ClearAsync(CancellationToken.None);
            Assert.False(File.Exists(path));
            Assert.Null(await store.TryReadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryRead_ReturnsNull_WhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "vc-secret-tests", Guid.NewGuid().ToString("N"), "ai.key");
        var store = new DpapiSecretStore(path, NullLogger<DpapiSecretStore>.Instance);

        Assert.Null(await store.TryReadAsync(CancellationToken.None));
    }
}
