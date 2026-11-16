namespace VocabularyCoach.Application.Backup;

/// <summary>数据库备份与恢复（SQLite 在线备份，恢复后需要重启应用）。</summary>
public interface IDatabaseBackupService
{
    /// <summary>把当前数据库备份到指定文件（覆盖已有文件）。</summary>
    Task BackupAsync(string destinationPath, CancellationToken cancellationToken);

    /// <summary>用备份文件覆盖当前数据库；调用方应提示用户重启应用。</summary>
    Task RestoreAsync(string sourcePath, CancellationToken cancellationToken);
}
