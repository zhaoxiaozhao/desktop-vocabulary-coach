namespace VocabularyCoach.Infrastructure.Persistence;

/// <summary>当前数据库文件位置（便于测试时指向临时文件）。</summary>
public sealed record DatabaseLocation(string Path)
{
    public static DatabaseLocation Default => new(Diagnostics.AppPaths.DatabaseFile);
}
