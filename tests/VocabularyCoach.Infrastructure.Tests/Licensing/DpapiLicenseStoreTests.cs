using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Infrastructure.Licensing;

namespace VocabularyCoach.Infrastructure.Tests.Licensing;

public class DpapiLicenseStoreTests
{
    [Fact]
    public async Task WriteReadAndClear_RoundTripsWithoutPlaintext()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dvc-license-{Guid.NewGuid():N}.dat");

        var store = new DpapiLicenseStore(path, NullLogger<DpapiLicenseStore>.Instance);

        try
        {
            Assert.Null(await store.ReadAsync());

            await store.WriteAsync("DVC1.test.signature");
            Assert.Equal("DVC1.test.signature", await store.ReadAsync());

            var raw = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            Assert.DoesNotContain("DVC1", raw);

            await store.ClearAsync();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
