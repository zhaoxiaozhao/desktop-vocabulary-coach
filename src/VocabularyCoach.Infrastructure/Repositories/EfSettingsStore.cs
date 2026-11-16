using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Repositories;

/// <summary>AppSettings 键值存储实现。</summary>
public sealed class EfSettingsStore : ISettingsStore
{
    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;

    public EfSettingsStore(IDbContextFactory<VocabularyCoachDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.AppSettings
            .AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.AppSettings
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            db.AppSettings.Add(new AppSettingRecord
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            record.Value = value;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
