namespace VocabularyCoach.Infrastructure.Diagnostics;

/// <summary>
/// 应用数据路径。所有用户数据都放在 %LocalAppData%\DesktopVocabularyCoach，
/// 绝不写入安装目录。
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "DesktopVocabularyCoach";

    /// <summary>本地应用数据根目录。</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>SQLite 数据库路径（Sprint 2 使用）。</summary>
    public static string DatabaseFile { get; } = Path.Combine(RootDirectory, "vocabulary.db");

    /// <summary>日志目录。</summary>
    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

    /// <summary>确保目录存在，返回目录路径。</summary>
    public static string EnsureDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
