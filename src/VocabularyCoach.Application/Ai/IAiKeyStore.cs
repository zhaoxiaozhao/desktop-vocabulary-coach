namespace VocabularyCoach.Application.Ai;

/// <summary>按连接 Id 保存 API Key（DPAPI 加密）。</summary>
public interface IAiKeyStore
{
    Task<string?> GetAsync(string connectionId, CancellationToken cancellationToken = default);

    Task SetAsync(
        string connectionId,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string connectionId, CancellationToken cancellationToken = default);
}
