using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Infrastructure.Dictionary;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Mapping;

namespace VocabularyCoach.Infrastructure.Repositories;

/// <summary>基于 SQLite 的词库查询实现。</summary>
public sealed class EfWordLibrary : IWordLibrary
{
    private const int CandidateWindow = 300;

    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;

    public EfWordLibrary(IDbContextFactory<VocabularyCoachDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<WordCard?> FindByWordAsync(string word, CancellationToken cancellationToken = default)
    {
        var normalized = WordLibraryImporter.Normalize(word);
        if (normalized.Length == 0)
        {
            return null;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.Words
            .AsNoTracking()
            .Include(x => x.Definitions)
            .FirstOrDefaultAsync(x => x.NormalizedWord == normalized, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : WordCardMapper.ToCard(record);
    }

    public async Task<WordCard?> GetNextNewWordAsync(int maxDifficulty, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var candidateIds = await db.Words
            .AsNoTracking()
            .Where(x => x.Difficulty <= maxDifficulty &&
                        !db.UserWordProgress.Any(p => p.WordId == x.Id))
            .OrderBy(x => x.FrequencyRank == null)
            .ThenBy(x => x.FrequencyRank)
            .ThenBy(x => x.Difficulty)
            .Select(x => x.Id)
            .Take(CandidateWindow)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidateIds.Count > 0)
        {
            var id = candidateIds[Random.Shared.Next(candidateIds.Count)];
            return await LoadCardAsync(db, id, cancellationToken).ConfigureAwait(false);
        }

        var hasUnlearnedWords = await db.Words
            .AsNoTracking()
            .AnyAsync(x => !db.UserWordProgress.Any(p => p.WordId == x.Id), cancellationToken)
            .ConfigureAwait(false);

        if (hasUnlearnedWords)
        {
            // 难度上限内没有未学词，但词库仍有更高难度的新词：不回退到已学词，
            // 由调用方决定是否放宽难度（Sprint 4 推荐器会处理）。
            return null;
        }

        // 所有词都已学过：退化为复习最久未出现的词（Sprint 4 的推荐器会替换这段逻辑）。
        var fallbackWordId = await db.UserWordProgress
            .AsNoTracking()
            .OrderBy(x => x.LastReviewedAt)
            .ThenBy(x => x.UpdatedAt)
            .Select(x => (int?)x.WordId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return fallbackWordId is null
            ? null
            : await LoadCardAsync(db, fallbackWordId.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetWordCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Words.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetDistractorDefinitionsAsync(
        int excludeWordId,
        int count,
        int preferredDifficulty,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return [];
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (from word in db.Words.AsNoTracking()
                          join definition in db.WordDefinitions.AsNoTracking() on word.Id equals definition.WordId
                          where word.Id != excludeWordId && definition.DefinitionZh != string.Empty
                          orderby word.Difficulty == preferredDifficulty ? 0 : 1, EF.Functions.Random()
                          select definition.DefinitionZh)
                         .Take(Math.Max(count * 4, count))
                         .ToListAsync(cancellationToken)
                         .ConfigureAwait(false);

        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetDistractorWordsAsync(
        int excludeWordId,
        string correctDefinition,
        int count,
        int preferredDifficulty,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return [];
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Words
            .AsNoTracking()
            .Where(x => x.Id != excludeWordId &&
                        x.Word != string.Empty &&
                        !db.WordDefinitions.Any(d =>
                            d.WordId == x.Id && d.DefinitionZh == correctDefinition))
            .OrderBy(x => x.Difficulty == preferredDifficulty ? 0 : 1)
            .ThenBy(x => EF.Functions.Random())
            .Select(x => x.Word)
            .Take(Math.Max(count * 8, count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();
    }

    private static async Task<WordCard?> LoadCardAsync(
        VocabularyCoachDbContext db,
        int wordId,
        CancellationToken cancellationToken)
    {
        var record = await db.Words
            .AsNoTracking()
            .Include(x => x.Definitions)
            .FirstOrDefaultAsync(x => x.Id == wordId, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : WordCardMapper.ToCard(record);
    }
}
