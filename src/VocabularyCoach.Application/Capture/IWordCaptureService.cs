using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Capture;

/// <summary>捕获检查结果，用于捕获确认弹窗。</summary>
public sealed record CapturePreview(
    bool IsValid,
    string? Error,
    string OriginalText,
    string NormalizedWord,
    bool ExistsInLibrary,
    bool AlreadyLearning,
    WordCard? Card,
    string? SuggestedDefinition);

public enum CaptureOutcome
{
    Added = 1,
    AlreadyLearning = 2,
    NeedsDefinition = 3,
    Invalid = 4,
}

public sealed record CaptureResult(CaptureOutcome Outcome, string Word, string? Message);

/// <summary>生词本中的一条记录。</summary>
public sealed record CapturedWord(
    int WordId,
    string Word,
    string? Phonetic,
    string? DefinitionZh,
    DateTimeOffset CapturedAt,
    int CaptureCount,
    bool IsLearning)
{
    public string CapturedAtText => CapturedAt.ToLocalTime().ToString("MM-dd HH:mm");

    public string CaptureText => $"捕获 {CaptureCount} 次";
}

/// <summary>生词捕获：检查、入库、生词本查询。</summary>
public interface IWordCaptureService
{
    Task<CapturePreview> InspectAsync(string rawText, CancellationToken cancellationToken);

    Task<CaptureResult> AddAsync(
        string rawText,
        string? definitionZh,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CapturedWord>> GetCapturedAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>捕获相关的持久化操作。</summary>
public interface IWordCaptureStore
{
    Task<bool> HasProgressAsync(int wordId, CancellationToken cancellationToken);

    Task<int> CreateWordWithProgressAsync(
        string word,
        string normalizedWord,
        string definitionZh,
        CancellationToken cancellationToken);

    Task EnsureDueNowAsync(int wordId, CancellationToken cancellationToken);

    Task RecordCaptureAsync(
        string rawText,
        string normalizedWord,
        int wordId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CapturedWord>> GetCapturedAsync(int limit, CancellationToken cancellationToken);
}
