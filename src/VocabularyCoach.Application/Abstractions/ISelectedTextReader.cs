namespace VocabularyCoach.Application.Abstractions;

/// <summary>读取当前前台应用中选中的文字（用于生词捕获）。</summary>
public interface ISelectedTextReader
{
    /// <summary>读取选区；无法读取时返回 null。不得记录选中的内容。</summary>
    Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken);
}
