using Microsoft.EntityFrameworkCore;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;
using VocabularyCoach.Infrastructure.Persistence.Mapping;

namespace VocabularyCoach.Infrastructure.Repositories;

/// <summary>复习进度、学习会话与到期查询的 SQLite 实现。</summary>
public sealed class EfLearningStore : ILearningStore
{
    private const int DefaultDifficulty = 2;

    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;

    public EfLearningStore(IDbContextFactory<VocabularyCoachDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<ReviewState?> GetReviewStateAsync(int wordId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var progress = await db.UserWordProgress
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WordId == wordId, cancellationToken)
            .ConfigureAwait(false);

        return progress is null
            ? null
            : new ReviewState
            {
                Repetition = progress.Repetition,
                EaseFactor = progress.EaseFactor,
                IntervalDays = progress.IntervalDays,
                LapseCount = progress.LapseCount,
                Status = progress.Status,
                DueAt = progress.DueAt,
            };
    }

    public async Task SaveCardOutcomeAsync(CardOutcome outcome, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (outcome.Schedule is { } schedule && outcome.Feedback is { } feedback)
        {
            var progress = await db.UserWordProgress
                .FirstOrDefaultAsync(x => x.WordId == outcome.WordId, cancellationToken)
                .ConfigureAwait(false);

            if (progress is null)
            {
                progress = new UserWordProgressRecord
                {
                    WordId = outcome.WordId,
                    EaseFactor = schedule.EaseFactor,
                };
                db.UserWordProgress.Add(progress);
            }

            progress.Status = schedule.Status;
            progress.Repetition = schedule.Repetition;
            progress.EaseFactor = schedule.EaseFactor;
            progress.IntervalDays = schedule.IntervalDays;
            progress.DueAt = schedule.DueAt;
            progress.LastReviewedAt = outcome.RespondedAt;
            progress.LapseCount = schedule.LapseCount;
            progress.UpdatedAt = outcome.RespondedAt;

            db.ReviewLogs.Add(new ReviewLogRecord
            {
                WordId = outcome.WordId,
                Rating = schedule.Rating,
                UserFeedback = feedback,
                ReviewedAt = outcome.RespondedAt,
                PreviousDueAt = outcome.PreviousDueAt,
                NextDueAt = schedule.DueAt,
                Source = outcome.Source,
            });
        }

        db.LearningSessions.Add(new LearningSessionRecord
        {
            WordId = outcome.WordId,
            ShownAt = outcome.ShownAt,
            RespondedAt = outcome.RespondedAt,
            Action = outcome.Result,
            DurationMs = outcome.DurationMs,
            Source = outcome.Source,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DueReview>> GetDueReviewsAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (from progress in db.UserWordProgress.AsNoTracking()
                          join word in db.Words.AsNoTracking().Include(x => x.Definitions)
                              on progress.WordId equals word.Id
                          where progress.DueAt != null && progress.DueAt <= nowUtc
                          orderby progress.DueAt
                          select new { Word = word, progress.DueAt })
                         .Take(Math.Max(1, limit))
                         .ToListAsync(cancellationToken)
                         .ConfigureAwait(false);

        return rows
            .Where(x => x.DueAt is not null)
            .Select(x => new DueReview(WordCardMapper.ToCard(x.Word), x.DueAt!.Value))
            .ToList();
    }

    public async Task<int> GetCurrentDifficultyAsync(CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(cancellationToken).ConfigureAwait(false);
        return Math.Clamp(profile.CurrentDifficulty, 1, 4);
    }

    public async Task<LearningProfileSnapshot> GetProfileAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var profile = await db.UserLearningProfiles
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return profile is null
            ? new LearningProfileSnapshot()
            : new LearningProfileSnapshot
            {
                CurrentDifficulty = profile.CurrentDifficulty,
                PreferredNewWordRatio = profile.PreferredNewWordRatio,
                RecentSampleSize = profile.RecentSampleSize,
                RecentRecognized = profile.RecentRecognized,
                RecentVague = profile.RecentVague,
                RecentUnknown = profile.RecentUnknown,
            };
    }

    public async Task SaveProfileAsync(LearningProfileSnapshot profile, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await db.UserLearningProfiles
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            record = new UserLearningProfileRecord();
            db.UserLearningProfiles.Add(record);
        }

        record.CurrentDifficulty = Math.Clamp(profile.CurrentDifficulty, 1, 4);
        record.PreferredNewWordRatio = profile.PreferredNewWordRatio is { } ratio
            ? Math.Clamp(ratio, 0, 1)
            : null;
        record.RecentSampleSize = Math.Max(0, profile.RecentSampleSize);
        record.RecentRecognized = Math.Max(0, profile.RecentRecognized);
        record.RecentVague = Math.Max(0, profile.RecentVague);
        record.RecentUnknown = Math.Max(0, profile.RecentUnknown);
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WordFeedback>> GetRecentMasteryFeedbackAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.ReviewLogs
            .AsNoTracking()
            .OrderByDescending(x => x.ReviewedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(x => x.UserFeedback)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountDueReviewsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.UserWordProgress
            .AsNoTracking()
            .CountAsync(x => x.DueAt != null && x.DueAt <= nowUtc, cancellationToken)
            .ConfigureAwait(false);
    }
}
