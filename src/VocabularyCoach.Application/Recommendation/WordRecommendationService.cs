using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Recommendation;

/// <summary>
/// Smart 推荐规则（可解释、无机器学习）：
/// 1. 有到期复习词时优先复习，最逾期的排在最前；
/// 2. 复习压力低且未超过逾期宽限时，按目标比例混合新词；
/// 3. 没有到期复习时按当前难度推荐新词。
/// 逾期复习永远不会因为凑比例被跳过。
/// 推荐模式与难度设置从 <see cref="ISettingsService"/> 实时读取。
/// </summary>
public sealed class WordRecommendationService : IWordCardSource
{
    private readonly ILearningStore _learningStore;
    private readonly IWordLibrary _wordLibrary;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WordRecommendationService> _logger;

    public WordRecommendationService(
        ILearningStore learningStore,
        IWordLibrary wordLibrary,
        ISettingsService settings,
        TimeProvider timeProvider,
        ILogger<WordRecommendationService> logger)
    {
        _learningStore = learningStore;
        _wordLibrary = wordLibrary;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WordCard?> GetNextCardAsync(CancellationToken cancellationToken)
    {
        var mode = _settings.Current.RecommendationMode;
        var profile = await _learningStore.GetProfileAsync(cancellationToken).ConfigureAwait(false);
        var options = _settings.Recommendation;

        if (mode != RecommendationMode.NewWordsOnly)
        {
            var now = _timeProvider.GetUtcNow();
            var dueReviews = await _learningStore
                .GetDueReviewsAsync(now, Math.Max(1, options.ReviewCandidateLimit), cancellationToken)
                .ConfigureAwait(false);

            if (dueReviews.Count > 0)
            {
                var oldest = dueReviews[0];
                var overdue = now - oldest.DueAt;
                var lowPressure = dueReviews.Count <= Math.Max(0, options.ReviewPressureThreshold);
                var targetNewWordRatio = profile.PreferredNewWordRatio ?? _settings.Current.NewWordRatio;

                if (mode == RecommendationMode.Smart &&
                    lowPressure &&
                    overdue < TimeSpan.FromHours(Math.Max(0, options.OverdueGraceHours)) &&
                    Random.Shared.NextDouble() < Math.Clamp(targetNewWordRatio, 0, 1))
                {
                    var newWord = await GetNewWordAsync(profile, cancellationToken).ConfigureAwait(false);
                    if (newWord is not null)
                    {
                        _logger.LogInformation(
                            "推荐新词 {Word}（复习压力低：{DueCount} 个到期，目标新词比例 {Ratio:P0}）。",
                            newWord.Word,
                            dueReviews.Count,
                            targetNewWordRatio);
                        return newWord;
                    }
                }

                _logger.LogInformation(
                    "推荐复习 {Word}（逾期 {Overdue:0.#} 小时，队列 {DueCount} 个）。",
                    oldest.Card.Word,
                    overdue.TotalHours,
                    dueReviews.Count);
                return oldest.Card;
            }
        }

        if (mode == RecommendationMode.ReviewOnly)
        {
            _logger.LogInformation("推荐模式为「仅复习」，当前没有到期单词。");
            return null;
        }

        return await GetNewWordAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WordCard?> GetNewWordAsync(
        LearningProfileSnapshot profile,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var difficulty = settings.DifficultyMode == DifficultyMode.Manual
            ? settings.ManualDifficulty
            : profile.CurrentDifficulty;
        var maxDifficulty = Math.Clamp(
            settings.DifficultyMode == DifficultyMode.Manual ? difficulty : difficulty + 1,
            1,
            4);

        var card = await _wordLibrary.GetNextNewWordAsync(maxDifficulty, cancellationToken).ConfigureAwait(false);

        if (card is not null)
        {
            _logger.LogInformation("推荐新词 {Word}（难度上限 {MaxDifficulty}）。", card.Word, maxDifficulty);
        }

        return card;
    }
}
