using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Adaptive;

/// <summary>用户学习档案快照（难度 + 最近反馈统计）。</summary>
public sealed record LearningProfileSnapshot
{
    public int CurrentDifficulty { get; init; } = 2;

    /// <summary>自适应调整后的新词比例偏好；null 表示使用设置中的基础比例。</summary>
    public double? PreferredNewWordRatio { get; init; }

    public int RecentSampleSize { get; init; }

    public int RecentRecognized { get; init; }

    public int RecentVague { get; init; }

    public int RecentUnknown { get; init; }
}

/// <summary>
/// 自适应难度规则：
/// - 最近 20 次有效反馈中，认识 &gt; 80% 且不认识 &lt; 10%：难度 +1 档，新词比例 +0.05；
/// - 不认识 &gt; 35%：难度 -1 档，新词比例 -0.1（增加复习倾向）；
/// - 其他情况保持不变；每次最多移动一档，样本不足 10 次不调整。
/// </summary>
public interface IAdaptiveDifficultyService
{
    /// <summary>根据最近反馈更新难度与倾向，返回更新后的难度。</summary>
    Task<int> ApplyAsync(CancellationToken cancellationToken);

    /// <summary>清除自适应产生的新词比例偏好，让用户手动设置立即生效。</summary>
    Task ResetPreferenceAsync(CancellationToken cancellationToken);
}

public sealed class AdaptiveDifficultyService : IAdaptiveDifficultyService
{
    public const int RecentWindowSize = 20;
    public const int MinimumSampleSize = 10;
    public const double HighRecognitionRatio = 0.80;
    public const double LowUnknownRatio = 0.10;
    public const double HighUnknownRatio = 0.35;

    private readonly ILearningStore _learningStore;
    private readonly ISettingsService _settings;
    private readonly ILogger<AdaptiveDifficultyService> _logger;

    public AdaptiveDifficultyService(
        ILearningStore learningStore,
        ISettingsService settings,
        ILogger<AdaptiveDifficultyService> logger)
    {
        _learningStore = learningStore;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        var profile = await _learningStore.GetProfileAsync(cancellationToken).ConfigureAwait(false);
        var recent = await _learningStore
            .GetRecentMasteryFeedbackAsync(RecentWindowSize, cancellationToken)
            .ConfigureAwait(false);

        var sampleSize = recent.Count;
        var recognized = recent.Count(x => x == WordFeedback.Recognized);
        var vague = recent.Count(x => x == WordFeedback.Vague);
        var unknown = recent.Count(x => x == WordFeedback.Unknown);

        var difficulty = profile.CurrentDifficulty;
        var preferredRatio = profile.PreferredNewWordRatio;

        if (_settings.Current.DifficultyMode == DifficultyMode.Adaptive &&
            sampleSize >= MinimumSampleSize)
        {
            var recognizedRatio = recognized / (double)sampleSize;
            var unknownRatio = unknown / (double)sampleSize;

            if (recognizedRatio > HighRecognitionRatio && unknownRatio < LowUnknownRatio)
            {
                if (difficulty < 4)
                {
                    difficulty++;
                    preferredRatio = Math.Min(0.8, (preferredRatio ?? _settings.Current.NewWordRatio) + 0.05);
                    _logger.LogInformation(
                        "自适应难度上调至 {Difficulty}（认识率 {Recognized:P0}，不认识率 {Unknown:P0}）。",
                        difficulty,
                        recognizedRatio,
                        unknownRatio);
                }
            }
            else if (unknownRatio > HighUnknownRatio)
            {
                if (difficulty > 1)
                {
                    difficulty--;
                    preferredRatio = Math.Max(0.2, (preferredRatio ?? _settings.Current.NewWordRatio) - 0.1);
                    _logger.LogInformation(
                        "自适应难度下调至 {Difficulty}（不认识率 {Unknown:P0}）。",
                        difficulty,
                        unknownRatio);
                }
            }
        }

        await _learningStore.SaveProfileAsync(
                new LearningProfileSnapshot
                {
                    CurrentDifficulty = difficulty,
                    PreferredNewWordRatio = preferredRatio,
                    RecentSampleSize = sampleSize,
                    RecentRecognized = recognized,
                    RecentVague = vague,
                    RecentUnknown = unknown,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return difficulty;
    }

    public async Task ResetPreferenceAsync(CancellationToken cancellationToken)
    {
        var profile = await _learningStore.GetProfileAsync(cancellationToken).ConfigureAwait(false);

        if (profile.PreferredNewWordRatio is null)
        {
            return;
        }

        await _learningStore.SaveProfileAsync(
                profile with { PreferredNewWordRatio = null },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("已清除自适应新词比例偏好，使用用户设置的 {Ratio:P0}。", _settings.Current.NewWordRatio);
    }
}
