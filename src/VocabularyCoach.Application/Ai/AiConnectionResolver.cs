using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.Application.Ai;

/// <summary>一次 AI 请求可用的连接与 Key。</summary>
public sealed record AiConnectionContext(AiConnection Connection, string ApiKey);

/// <summary>按手动/Auto 模式选择候选连接。</summary>
public interface IAiConnectionResolver
{
    Task<IReadOnlyList<AiConnectionContext>> GetCandidatesAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class AiConnectionResolver : IAiConnectionResolver
{
    private readonly IAiKeyStore _keyStore;

    public AiConnectionResolver(IAiKeyStore keyStore)
    {
        _keyStore = keyStore;
    }

    public async Task<IReadOnlyList<AiConnectionContext>> GetCandidatesAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        AiConnectionSettings.Ensure(settings);

        IEnumerable<AiConnection> connections = settings.AiAutoMode
            ? settings.AiConnections.Where(x => x.Enabled)
            : settings.AiConnections.Where(x =>
                x.Enabled
                && string.Equals(
                    x.Id,
                    settings.ActiveAiConnectionId,
                    StringComparison.OrdinalIgnoreCase));

        var result = new List<AiConnectionContext>();
        foreach (var connection in connections)
        {
            var key = await _keyStore
                .GetAsync(connection.Id, cancellationToken)
                .ConfigureAwait(false);

            result.Add(new AiConnectionContext(connection, key ?? string.Empty));
        }

        return result;
    }
}
