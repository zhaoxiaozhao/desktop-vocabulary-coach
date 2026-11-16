using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Recommendation;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Quiz;

/// <summary>为单词卡片生成“选择正确释义”的题目。</summary>
public interface IWordQuizGenerator
{
    Task<WordQuiz?> TryCreateAsync(WordCard card, CancellationToken cancellationToken);
}

public sealed class WordQuizGenerator : IWordQuizGenerator
{
    private const int MaxDistractors = 3;

    private readonly IWordLibrary _wordLibrary;

    public WordQuizGenerator(IWordLibrary wordLibrary)
    {
        _wordLibrary = wordLibrary;
    }

    public async Task<WordQuiz?> TryCreateAsync(WordCard card, CancellationToken cancellationToken)
    {
        var correctDefinition = card.PrimaryDefinition?.DefinitionZh;
        if (string.IsNullOrWhiteSpace(correctDefinition) || card.Id <= 0)
        {
            return null;
        }

        var distractors = await _wordLibrary
            .GetDistractorDefinitionsAsync(card.Id, MaxDistractors + 2, card.Difficulty, cancellationToken)
            .ConfigureAwait(false);

        var uniqueDistractors = distractors
            .Where(x => !string.Equals(x, correctDefinition, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxDistractors)
            .ToList();

        if (uniqueDistractors.Count < MaxDistractors)
        {
            return null;
        }

        var choices = new List<WordChoice>(uniqueDistractors.Count + 1)
        {
            new(correctDefinition, IsCorrect: true),
        };

        choices.AddRange(uniqueDistractors.Select(x => new WordChoice(x, IsCorrect: false)));

        return new WordQuiz(choices.OrderBy(_ => Random.Shared.Next()).ToList());
    }
}

/// <summary>
/// 根据设置给推荐结果附加答题题目。选择释义不可用时自动回退到自我评估，
/// 不影响任何核心学习流程。
/// </summary>
public sealed class QuizWordCardSource : IWordCardSource
{
    private readonly WordRecommendationService _inner;
    private readonly IWordQuizGenerator _quizGenerator;
    private readonly ISettingsService _settings;
    private readonly ILogger<QuizWordCardSource> _logger;

    public QuizWordCardSource(
        WordRecommendationService inner,
        IWordQuizGenerator quizGenerator,
        ISettingsService settings,
        ILogger<QuizWordCardSource> logger)
    {
        _inner = inner;
        _quizGenerator = quizGenerator;
        _settings = settings;
        _logger = logger;
    }

    public async Task<WordCard?> GetNextCardAsync(CancellationToken cancellationToken)
    {
        var card = await _inner.GetNextCardAsync(cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return null;
        }

        var mode = _settings.Current.InteractionMode;
        var useQuiz = mode switch
        {
            InteractionMode.ChoiceQuiz => true,
            InteractionMode.Mixed => Random.Shared.NextDouble() < 0.5,
            _ => false,
        };

        if (!useQuiz)
        {
            return card;
        }

        var quiz = await _quizGenerator.TryCreateAsync(card, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            _logger.LogDebug("无法为 {Word} 生成选择题，回退到自我评估。", card.Word);
            return card;
        }

        _logger.LogInformation("选择释义模式：{Word}（{Count} 个选项）。", card.Word, quiz.Choices.Count);
        return card with { Quiz = quiz };
    }
}
