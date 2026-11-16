using Microsoft.Data.Sqlite;
using VocabularyCoach.Application.Backup;

namespace VocabularyCoach.Infrastructure.Persistence;

/// <summary>基于 SQLite 在线备份 API 的数据库备份/恢复。</summary>
public sealed class SqliteDatabaseBackupService : IDatabaseBackupService
{
    private readonly DatabaseLocation _databaseLocation;

    public SqliteDatabaseBackupService(DatabaseLocation databaseLocation)
    {
        _databaseLocation = databaseLocation;
    }

    public async Task BackupAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        await using var source = new SqliteConnection($"Data Source={_databaseLocation.Path}");
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var destination = new SqliteConnection($"Data Source={destinationPath}");
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);

        source.BackupDatabase(destination);
    }

    public async Task RestoreAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("备份文件不存在。", sourcePath);
        }

        await ValidateBackupAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        // 关闭连接池，确保没有连接占用数据库文件。
        SqliteConnection.ClearAllPools();

        File.Copy(sourcePath, _databaseLocation.Path, overwrite: true);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = _databaseLocation.Path + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }

    private static async Task ValidateBackupAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Words';";
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        if (result == 0)
        {
            throw new InvalidDataException("所选文件不是本应用的数据库备份（缺少 Words 表）。");
        }
    }
}
