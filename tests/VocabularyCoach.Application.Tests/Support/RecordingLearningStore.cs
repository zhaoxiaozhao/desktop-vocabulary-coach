using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class RecordingLearningStore : ILearningStore
{
    public List<CardOutcome> SavedOutcomes { get; } = [];

    public ReviewState? State { get; set; }

    public IReadOnlyList<DueReview> DueReviews { get; set; } = [];

    public LearningProfileSnapshot Profile { get; set; } = new();

    public IReadOnlyList<WordFeedback> RecentMasteryFeedback { get; set; } = [];

    public int CurrentDifficulty { get; set; } = 2;

    public Task<ReviewState?> GetReviewStateAsync(int wordId, CancellationToken cancellationToken) =>
        Task.FromResult(State);

    public Task SaveCardOutcomeAsync(CardOutcome outcome, CancellationToken cancellationToken)
    {
        SavedOutcomes.Add(outcome);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DueReview>> GetDueReviewsAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult(DueReviews);

    public Task<int> GetCurrentDifficultyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentDifficulty);

    public Task<int> CountDueReviewsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult(DueReviews.Count);

    public Task<LearningProfileSnapshot> GetProfileAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Profile);

    public Task SaveProfileAsync(LearningProfileSnapshot profile, CancellationToken cancellationToken)
    {
        Profile = profile;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WordFeedback>> GetRecentMasteryFeedbackAsync(
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult(RecentMasteryFeedback);
}

internal sealed class FakeWordLibrary : VocabularyCoach.Application.Abstractions.IWordLibrary
{
    public WordCard? NextNewWord { get; set; }

    public WordCard? FoundWord { get; set; }

    public int NewWordCallCount { get; private set; }

    public int LastMaxDifficulty { get; private set; }

    public IReadOnlyList<string> Distractors { get; set; } = [];

    public IReadOnlyList<string> DistractorWords { get; set; } = [];

    public Task<WordCard?> FindByWordAsync(string word, CancellationToken cancellationToken = default) =>
        Task.FromResult(FoundWord);

    public Task<WordCard?> GetNextNewWordAsync(int maxDifficulty, CancellationToken cancellationToken = default)
    {
        NewWordCallCount++;
        LastMaxDifficulty = maxDifficulty;
        return Task.FromResult(NextNewWord);
    }

    public Task<int> GetWordCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<string>> GetDistractorDefinitionsAsync(
        int excludeWordId,
        int count,
        int preferredDifficulty,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Distractors);

    public Task<IReadOnlyList<string>> GetDistractorWordsAsync(
        int excludeWordId,
        string correctDefinition,
        int preferredLength,
        int count,
        int preferredDifficulty,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(DistractorWords);
}
