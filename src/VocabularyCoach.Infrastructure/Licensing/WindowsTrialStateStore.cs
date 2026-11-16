using System.Text.Json;
using Microsoft.Win32;
using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.Infrastructure.Licensing;

/// <summary>
/// 试用状态冗余保存：%LocalAppData%\DesktopVocabularyCoach\trial.json 与当前用户注册表。
/// 读取时取最早的首启时间，降低删除单个位置重置试用的概率。
/// </summary>
public sealed class WindowsTrialStateStore : ITrialStateStore
{
    private const string RegistryPath = @"Software\DesktopVocabularyCoach";
    private const string FirstValueName = "TrialFirstStartedAt";
    private const string LastValueName = "TrialLastSeenAt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public WindowsTrialStateStore(string filePath)
    {
        _filePath = filePath;
    }

    public Task<TrialState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset? first = null;
        DateTimeOffset? last = null;

        if (TryReadFile(out var fileState))
        {
            first = fileState.FirstStartedAt;
            last = fileState.LastSeenAt;
        }

        if (TryReadRegistry(out var registryState))
        {
            first = first is null || registryState.FirstStartedAt < first
                ? registryState.FirstStartedAt
                : first;
            last = last is null || registryState.LastSeenAt > last
                ? registryState.LastSeenAt
                : last;
        }

        return Task.FromResult(
            first is null || last is null
                ? null
                : new TrialState(first.Value, last.Value));
    }

    public Task SaveAsync(TrialState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, overwrite: true);

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key?.SetValue(FirstValueName, state.FirstStartedAt.ToString("O"), RegistryValueKind.String);
            key?.SetValue(LastValueName, state.LastSeenAt.ToString("O"), RegistryValueKind.String);
        }
        catch (Exception)
        {
            // 注册表写入失败时至少保留文件记录；不阻塞启动。
        }

        return Task.CompletedTask;
    }

    private bool TryReadFile(out TrialState state)
    {
        state = default!;

        try
        {
            if (!File.Exists(_filePath))
            {
                return false;
            }

            var parsed = JsonSerializer.Deserialize<TrialState>(
                File.ReadAllText(_filePath),
                JsonOptions);

            if (parsed is null || parsed.FirstStartedAt == default || parsed.LastSeenAt == default)
            {
                return false;
            }

            state = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadRegistry(out TrialState state)
    {
        state = default!;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            if (key?.GetValue(FirstValueName) is not string firstText
                || key.GetValue(LastValueName) is not string lastText)
            {
                return false;
            }

            if (!DateTimeOffset.TryParse(firstText, out var first)
                || !DateTimeOffset.TryParse(lastText, out var last))
            {
                return false;
            }

            state = new TrialState(first, last);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
