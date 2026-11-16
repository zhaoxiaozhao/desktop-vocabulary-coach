using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Infrastructure.Persistence;

namespace VocabularyCoach.Infrastructure.Tests.Support;

/// <summary>每个测试使用独立的临时 SQLite 文件，避免相互污染。</summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    public SqliteTestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "vc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Factory = new TestDbContextFactory(Path.Combine(_directory, "test.db"));
    }

    public IDbContextFactory<VocabularyCoachDbContext> Factory { get; }

    public async Task InitializeAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 文件仍被占用时留给系统清理。
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<VocabularyCoachDbContext>
    {
        private readonly DbContextOptions<VocabularyCoachDbContext> _options;

        public TestDbContextFactory(string path)
        {
            _options = new DbContextOptionsBuilder<VocabularyCoachDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
        }

        public VocabularyCoachDbContext CreateDbContext() => new(_options);
    }
}
