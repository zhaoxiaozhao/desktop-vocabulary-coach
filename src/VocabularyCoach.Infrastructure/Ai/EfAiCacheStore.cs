using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Ai;

/// <summary>AI 生成结果缓存（AiGenerationCache 表）。</summary>
public sealed class EfAiCacheStore : IAiCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;

    public EfAiCacheStore(IDbContextFactory<VocabularyCoachDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<WordEnrichment?> GetAsync(
        int wordId,
        string provider,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var content = await db.AiGenerationCache
            .AsNoTracking()
            .Where(x => x.WordId == wordId && x.Provider == provider)
            .Select(x => x.Content)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WordEnrichment>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        int wordId,
        string provider,
        string model,
        WordEnrichment enrichment,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.AiGenerationCache
            .FirstOrDefaultAsync(x => x.WordId == wordId && x.Provider == provider, cancellationToken)
            .ConfigureAwait(false);

        var content = JsonSerializer.Serialize(enrichment, JsonOptions);
        var providerKey = provider.Length > 64 ? provider[..64] : provider;

        if (record is null)
        {
            db.AiGenerationCache.Add(new AiGenerationCacheRecord
            {
                WordId = wordId,
                Provider = providerKey,
                Model = model.Length > 128 ? model[..128] : model,
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            record.Model = model.Length > 128 ? model[..128] : model;
            record.Content = content;
            record.CreatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
