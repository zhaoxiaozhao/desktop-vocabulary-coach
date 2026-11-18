using VocabularyCoach.Application.Ai;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class FakeAiKeyStore : IAiKeyStore
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public FakeAiKeyStore(string? defaultKey = null)
    {
        if (defaultKey is not null)
        {
            _keys["default"] = defaultKey;
        }
    }

    public Task<string?> GetAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_keys.GetValueOrDefault(connectionId));

    public Task SetAsync(
        string connectionId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        _keys[connectionId] = apiKey;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        _keys.Remove(connectionId);
        return Task.CompletedTask;
    }
}
