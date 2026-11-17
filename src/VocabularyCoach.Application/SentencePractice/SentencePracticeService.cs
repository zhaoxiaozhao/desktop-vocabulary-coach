using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.SentencePractice;

/// <summary>
/// 句子填空练习：从离线句库取句，AI 可用时优先用 AI 生成的句子；
/// 提交后按「认识/不认识」写入现有 SM-2 复习闭环。
/// </summary>
public sealed class SentencePracticeService : ISentencePracticeService
{
    private const int MaxSessionSize = 20;

    private readonly IWordLibrary _wordLibrary;
    private readonly ISentenceBank _sentenceBank;
    private readonly ISentencePracticeGenerator _aiGenerator;
    private readonly ISettingsService _settings;
    private readonly ILearningStore _learningStore;
    private readonly IReviewScheduler _reviewScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SentencePracticeService> _logger;

    public SentencePracticeService(
        IWordLibrary wordLibrary,
        ISentenceBank sentenceBank,
        ISentencePracticeGenerator aiGenerator,
        ISettingsService settings,
        ILearningStore learningStore,
        IReviewScheduler reviewScheduler,
        TimeProvider timeProvider,
        ILogger<SentencePracticeService> logger)
    {
        _wordLibrary = wordLibrary;
        _sentenceBank = sentenceBank;
        _aiGenerator = aiGenerator;
        _settings = settings;
        _learningStore = learningStore;
        _reviewScheduler = reviewScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SentencePracticeItem>> CreateSessionAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, MaxSessionSize);

        var entries = _sentenceBank.Entries
            .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ElementAt(Random.Shared.Next(group.Count())))
            .OrderBy(_ => Random.Shared.Next())
            .Take(take)
            .ToList();

        var items = new List<SentencePracticeItem>(entries.Count);
        var cards = new List<WordCard>(entries.Count);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var card = await _wordLibrary
                .FindByWordAsync(entry.Word, cancellationToken)
                .ConfigureAwait(false);

            // 句库会刻意包含 go / take / see 等高频口语词；
            // 词库为降低卡片噪音排除了最高频的 300 词，因此这里允许 Id=0：
            // 句子仍可练习，但不写入 SM-2 复习进度。
            var wordId = card?.Id ?? 0;
            var word = card?.Word ?? entry.Word;

            var options = BuildOptions(entry.Answer, entry.Distractors);
            if (options.Count < 4)
            {
                _logger.LogDebug("句子选项不足 4 个，跳过：{Word}", entry.Word);
                continue;
            }

            cards.Add(card ?? new WordCard
            {
                Id = 0,
                Word = word,
                Difficulty = 2,
            });

            items.Add(new SentencePracticeItem(
                wordId,
                word,
                entry.Sentence,
                entry.Answer,
                entry.AlternativeAnswers ?? [],
                entry.TranslationZh,
                entry.Pattern,
                entry.GrammarNote,
                options,
                FromAi: false));
        }

        if (cards.Count == 0)
        {
            return [];
        }

        if (!_settings.Current.AiEnabled)
        {
            return items;
        }

        try
        {
            var aiItems = await _aiGenerator
                .GenerateAsync(cards, cancellationToken)
                .ConfigureAwait(false);

            if (aiItems.Count == 0)
            {
                return items;
            }

            var aiByWord = aiItems
                .Where(x => x.WordId > 0 && x.Options.Count >= 4)
                .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var merged = items
                .Select(item => aiByWord.TryGetValue(item.Word, out var ai) ? ai : item)
                .ToList();

            _logger.LogInformation(
                "句子练习：{Total} 句，其中 AI 生成 {AiCount} 句。",
                merged.Count,
                merged.Count(x => x.FromAi));

            return merged;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 生成句子失败，使用离线句库。");
            return items;
        }
    }

    public async Task<SentencePracticeOutcome> SubmitAsync(
        SentencePracticeItem item,
        string answer,
        DateTimeOffset shownAt,
        CancellationToken cancellationToken = default)
    {
        var correct = IsCorrect(item, answer);
        var now = _timeProvider.GetUtcNow();

        if (item.WordId <= 0)
        {
            return new SentencePracticeOutcome(correct, item.Answer);
        }

        try
        {
            ReviewState? state = null;
            ReviewScheduleResult? schedule = null;

            try
            {
                state = await _learningStore
                    .GetReviewStateAsync(item.WordId, CancellationToken.None)
                    .ConfigureAwait(false);

                schedule = _reviewScheduler.Schedule(
                    state ?? ReviewState.New,
                    correct ? ReviewRating.Good : ReviewRating.Again,
                    now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取句子练习复习状态失败：{Word}", item.Word);
            }

            var durationMs = Math.Max(0, (int)(now - shownAt).TotalMilliseconds);
            var outcome = new CardOutcome(
                item.WordId,
                LearningSource.SentencePractice,
                shownAt,
                now,
                durationMs,
                correct ? WordCardResult.Recognized : WordCardResult.Unknown,
                correct ? WordFeedback.Recognized : WordFeedback.Unknown,
                schedule,
                state?.DueAt);

            await _learningStore
                .SaveCardOutcomeAsync(outcome, CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "句子练习：{Word}，{Result}，答案 {Answer}，下次复习 {NextDue:u}",
                item.Word,
                correct ? "正确" : "错误",
                item.Answer,
                schedule?.DueAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存句子练习结果失败：{Word}", item.Word);
        }

        return new SentencePracticeOutcome(correct, item.Answer);
    }

    private static bool IsCorrect(SentencePracticeItem item, string answer)
    {
        var normalized = Normalize(answer);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalized, Normalize(item.Answer), StringComparison.Ordinal))
        {
            return true;
        }

        return item.AlternativeAnswers.Any(
            x => string.Equals(normalized, Normalize(x), StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        return text.Trim(' ', '.', ',', '!', '?', ';', ':', '"', '\'');
    }

    private static IReadOnlyList<string> BuildOptions(
        string answer,
        IReadOnlyList<string> distractors)
    {
        var options = new List<string> { answer };
        options.AddRange(distractors);

        return options
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();
    }
}
