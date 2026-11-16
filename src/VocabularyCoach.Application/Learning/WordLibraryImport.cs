namespace VocabularyCoach.Application.Learning;

/// <summary>词库导入文件格式。</summary>
public enum WordImportFormat
{
    Json,
    Csv,
}

/// <summary>单条导入错误，便于用户定位文件问题。</summary>
public sealed record WordImportError(string Word, string Message);

/// <summary>一次词库导入的统计结果。</summary>
public sealed record WordImportResult(
    int Added,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<WordImportError> Errors)
{
    public int Total => Added + Updated + Skipped + Failed;

    public override string ToString() =>
        $"新增 {Added}，更新 {Updated}，跳过 {Skipped}，失败 {Failed}";
}

/// <summary>可重复执行的词库导入器：按规范化单词去重，不覆盖用户学习进度。</summary>
public interface IWordLibraryImporter
{
    Task<WordImportResult> ImportAsync(
        Stream stream,
        WordImportFormat format,
        CancellationToken cancellationToken = default);
}
