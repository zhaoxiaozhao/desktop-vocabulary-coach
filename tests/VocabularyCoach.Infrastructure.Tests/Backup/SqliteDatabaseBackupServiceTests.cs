using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Tests.Backup;

public class SqliteDatabaseBackupServiceTests
{
    [Fact]
    public async Task Backup_CreatesConsistentDatabaseCopy()
    {
        var directory = CreateTempDirectory();
        var sourcePath = Path.Combine(directory, "source.db");
        var backupPath = Path.Combine(directory, "backup.db");

        try
        {
            await CreateDatabaseWithWordAsync(sourcePath);

            var service = new SqliteDatabaseBackupService(new DatabaseLocation(sourcePath));
            await service.BackupAsync(backupPath, CancellationToken.None);

            Assert.True(File.Exists(backupPath));

            var options = new DbContextOptionsBuilder<VocabularyCoachDbContext>()
                .UseSqlite($"Data Source={backupPath}")
                .Options;
            await using var db = new VocabularyCoachDbContext(options);
            Assert.Equal(1, await db.Words.CountAsync());
            Assert.Equal("apple", await db.Words.Select(x => x.Word).SingleAsync());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Restore_ReplacesTargetDatabase()
    {
        var directory = CreateTempDirectory();
        var sourcePath = Path.Combine(directory, "source.db");
        var backupPath = Path.Combine(directory, "backup.db");
        var targetPath = Path.Combine(directory, "target.db");

        try
        {
            await CreateDatabaseWithWordAsync(sourcePath);
            await new SqliteDatabaseBackupService(new DatabaseLocation(sourcePath))
                .BackupAsync(backupPath, CancellationToken.None);

            await CreateDatabaseWithWordAsync(targetPath, "banana");

            var target = new SqliteDatabaseBackupService(new DatabaseLocation(targetPath));
            await target.RestoreAsync(backupPath, CancellationToken.None);

            var options = new DbContextOptionsBuilder<VocabularyCoachDbContext>()
                .UseSqlite($"Data Source={targetPath}")
                .Options;
            await using var db = new VocabularyCoachDbContext(options);
            Assert.Equal("apple", await db.Words.Select(x => x.Word).SingleAsync());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task Restore_RejectsNonApplicationDatabase()
    {
        var directory = CreateTempDirectory();
        var targetPath = Path.Combine(directory, "target.db");
        var invalidPath = Path.Combine(directory, "not-a-db.txt");

        try
        {
            await CreateDatabaseWithWordAsync(targetPath);
            await File.WriteAllTextAsync(invalidPath, "hello");

            var service = new SqliteDatabaseBackupService(new DatabaseLocation(targetPath));

            await Assert.ThrowsAnyAsync<Exception>(
                () => service.RestoreAsync(invalidPath, CancellationToken.None));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static async Task CreateDatabaseWithWordAsync(string path, string word = "apple")
    {
        var options = new DbContextOptionsBuilder<VocabularyCoachDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        await using var db = new VocabularyCoachDbContext(options);
        await db.Database.MigrateAsync();

        db.Words.Add(new WordRecord
        {
            Word = word,
            NormalizedWord = word,
            Difficulty = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vc-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Cleanup(string directory)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
