using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Capture;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Repositories;

/// <summary>生词捕获的 SQLite 实现。</summary>
public sealed class EfWordCaptureStore : IWordCaptureStore
{
    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;

    public EfWordCaptureStore(IDbContextFactory<VocabularyCoachDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<bool> HasProgressAsync(int wordId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.UserWordProgress
            .AsNoTracking()
            .AnyAsync(x => x.WordId == wordId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CreateWordWithProgressAsync(
        string word,
        string normalizedWord,
        string definitionZh,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var record = new WordRecord
        {
            Word = word,
            NormalizedWord = normalizedWord,
            Difficulty = 2,
            FrequencyRank = null,
            Source = "Captured",
            CreatedAt = now,
            UpdatedAt = now,
            Definitions =
            [
                new WordDefinitionRecord
                {
                    DefinitionZh = definitionZh,
                    SortOrder = 0,
                },
            ],
        };

        db.Words.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        db.UserWordProgress.Add(new UserWordProgressRecord
        {
            WordId = record.Id,
            Status = WordProgressStatus.New,
            EaseFactor = 2.5,
            DueAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return record.Id;
    }

    public async Task EnsureDueNowAsync(int wordId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var progress = await db.UserWordProgress
            .FirstOrDefaultAsync(x => x.WordId == wordId, cancellationToken)
            .ConfigureAwait(false);

        if (progress is null)
        {
            db.UserWordProgress.Add(new UserWordProgressRecord
            {
                WordId = wordId,
                Status = WordProgressStatus.New,
                EaseFactor = 2.5,
                DueAt = now,
                UpdatedAt = now,
            });
        }
        else if (progress.DueAt is null || progress.DueAt > now)
        {
            progress.DueAt = now;
            progress.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordCaptureAsync(
        string rawText,
        string normalizedWord,
        int wordId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var record = await db.WordCaptures
            .FirstOrDefaultAsync(x => x.NormalizedWord == normalizedWord, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            db.WordCaptures.Add(new WordCaptureRecord
            {
                WordId = wordId,
                RawText = rawText,
                NormalizedWord = normalizedWord,
                CapturedAt = now,
                CaptureCount = 1,
            });
        }
        else
        {
            record.WordId = wordId;
            record.RawText = rawText;
            record.CapturedAt = now;
            record.CaptureCount++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CapturedWord>> GetCapturedAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var captures = await db.WordCaptures
            .AsNoTracking()
            .Include(x => x.Word)!
            .ThenInclude(x => x!.Definitions)
            .OrderByDescending(x => x.CapturedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var wordIds = captures.Select(x => x.WordId).Distinct().ToList();
        var learningIds = await db.UserWordProgress
            .AsNoTracking()
            .Where(x => wordIds.Contains(x.WordId))
            .Select(x => x.WordId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return captures
            .Where(x => x.Word is not null)
            .Select(x => new CapturedWord(
                x.WordId,
                x.Word!.Word,
                x.Word.Phonetic,
                x.Word.Definitions.OrderBy(d => d.SortOrder).FirstOrDefault()?.DefinitionZh,
                x.CapturedAt,
                x.CaptureCount,
                learningIds.Contains(x.WordId)))
            .ToList();
    }
}
