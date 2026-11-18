using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Recommendation;
using VocabularyCoach.Application.SentencePractice;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Quiz;

/// <summary>为单词卡片生成选择题（看词选义或看义选词）。</summary>
public interface IWordQuizGenerator
{
    /// <summary>生成「看词选义」题目（兼容旧调用）。</summary>
    Task<WordQuiz?> TryCreateAsync(WordCard card, CancellationToken cancellationToken);

    /// <summary>
    /// 按方向生成题目。默认实现只支持正向；反向由具体实现覆盖。
    /// </summary>
    Task<WordQuiz?> TryCreateAsync(
        WordCard card,
        QuizDirection direction,
        CancellationToken cancellationToken) =>
        direction == QuizDirection.WordToDefinition
            ? TryCreateAsync(card, cancellationToken)
            : Task.FromResult<WordQuiz?>(null);
}

public sealed class WordQuizGenerator : IWordQuizGenerator
{
    private const int MaxDistractors = 3;

    private readonly IWordLibrary _wordLibrary;

    public WordQuizGenerator(IWordLibrary wordLibrary)
    {
        _wordLibrary = wordLibrary;
    }

    public Task<WordQuiz?> TryCreateAsync(WordCard card, CancellationToken cancellationToken) =>
        TryCreateAsync(card, QuizDirection.WordToDefinition, cancellationToken);

    public Task<WordQuiz?> TryCreateAsync(
        WordCard card,
        QuizDirection direction,
        CancellationToken cancellationToken) =>
        direction == QuizDirection.DefinitionToWord
            ? CreateReverseAsync(card, cancellationToken)
            : CreateForwardAsync(card, cancellationToken);

    private async Task<WordQuiz?> CreateForwardAsync(
        WordCard card,
        CancellationToken cancellationToken)
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

        return new WordQuiz(
            choices.OrderBy(_ => Random.Shared.Next()).ToList(),
            QuizDirection.WordToDefinition);
    }

    private async Task<WordQuiz?> CreateReverseAsync(
        WordCard card,
        CancellationToken cancellationToken)
    {
        var correctWord = card.Word;
        var correctDefinition = card.PrimaryDefinition?.DefinitionZh;
        if (string.IsNullOrWhiteSpace(correctWord)
            || string.IsNullOrWhiteSpace(correctDefinition)
            || card.Id <= 0)
        {
            return null;
        }

        var distractors = await _wordLibrary
            .GetDistractorWordsAsync(
                card.Id,
                correctDefinition,
                correctWord.Length,
                MaxDistractors + 2,
                card.Difficulty,
                cancellationToken)
            .ConfigureAwait(false);

        var uniqueDistractors = distractors
            .Where(x => !string.Equals(x, correctWord, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDistractors)
            .ToList();

        if (uniqueDistractors.Count < MaxDistractors)
        {
            return null;
        }

        var choices = new List<WordChoice>(uniqueDistractors.Count + 1)
        {
            new(correctWord, IsCorrect: true),
        };

        choices.AddRange(uniqueDistractors.Select(x => new WordChoice(x, IsCorrect: false)));

        return new WordQuiz(
            choices.OrderBy(_ => Random.Shared.Next()).ToList(),
            QuizDirection.DefinitionToWord);
    }
}

/// <summary>
/// 根据用户设置给推荐结果附加选择题；题目生成失败时自动回退到自我评估，
/// 不影响任何核心学习流程。
/// </summary>
public sealed class QuizWordCardSource : IWordCardSource
{
    private readonly WordRecommendationService _inner;
    private readonly IWordQuizGenerator _quizGenerator;
    private readonly IGeneratedSentenceStore _generatedSentences;
    private readonly ISettingsService _settings;
    private readonly ILogger<QuizWordCardSource> _logger;

    public QuizWordCardSource(
        WordRecommendationService inner,
        IWordQuizGenerator quizGenerator,
        IGeneratedSentenceStore generatedSentences,
        ISettingsService settings,
        ILogger<QuizWordCardSource> logger)
    {
        _inner = inner;
        _quizGenerator = quizGenerator;
        _generatedSentences = generatedSentences;
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

        if (card.Enrichment is null && card.Id > 0)
        {
            var generated = await _generatedSentences
                .GetRandomForWordAsync(card.Id, cancellationToken)
                .ConfigureAwait(false);

            if (generated is not null)
            {
                card = card with
                {
                    Enrichment = new WordEnrichment(
                        generated.ExampleSentence,
                        generated.TranslationZh,
                        generated.GrammarNote,
                        ConfusableNote: null),
                };
            }
        }

        var mode = _settings.Current.InteractionMode;

        // 混合模式：一半自我评估，一半选择题；选择题内部再随机正/反。
        var useQuiz = mode switch
        {
            InteractionMode.ChoiceQuiz or InteractionMode.ReverseQuiz => true,
            InteractionMode.Mixed => Random.Shared.NextDouble() < 0.5,
            _ => false,
        };

        if (!useQuiz)
        {
            return card;
        }

        var direction = mode switch
        {
            InteractionMode.ReverseQuiz => QuizDirection.DefinitionToWord,
            InteractionMode.ChoiceQuiz => QuizDirection.WordToDefinition,
            _ => Random.Shared.NextDouble() < 0.5
                ? QuizDirection.WordToDefinition
                : QuizDirection.DefinitionToWord,
        };

        var quiz = await _quizGenerator
            .TryCreateAsync(card, direction, cancellationToken)
            .ConfigureAwait(false);

        if (quiz is null)
        {
            _logger.LogDebug(
                "无法为 {Word} 生成 {Direction} 选择题，回退到自我评估。",
                card.Word,
                direction);
            return card;
        }

        _logger.LogInformation(
            "选择题模式（{Direction}）：{Word}（{Count} 个选项）。",
            direction,
            card.Word,
            quiz.Choices.Count);

        return card with { Quiz = quiz };
    }
}
