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
    private const string AiFallbackMessage = "AI 暂时不可用，已使用离线句库；10 分钟内不再尝试。";
    private const string AiCooldownMessage = "AI 暂时不可用，正在使用离线句库。";

    private static readonly TimeSpan AiFailureCooldown = TimeSpan.FromMinutes(10);

    private readonly IWordLibrary _wordLibrary;
    private readonly ISentenceBank _sentenceBank;
    private readonly IGeneratedSentenceStore _generatedSentences;
    private readonly ISentencePracticeGenerator _aiGenerator;
    private readonly ISettingsService _settings;
    private readonly ILearningStore _learningStore;
    private readonly IReviewScheduler _reviewScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SentencePracticeService> _logger;
    private readonly object _aiGate = new();

    private DateTimeOffset? _aiCooldownUntil;

    public SentencePracticeService(
        IWordLibrary wordLibrary,
        ISentenceBank sentenceBank,
        IGeneratedSentenceStore generatedSentences,
        ISentencePracticeGenerator aiGenerator,
        ISettingsService settings,
        ILearningStore learningStore,
        IReviewScheduler reviewScheduler,
        TimeProvider timeProvider,
        ILogger<SentencePracticeService> logger)
    {
        _wordLibrary = wordLibrary;
        _sentenceBank = sentenceBank;
        _generatedSentences = generatedSentences;
        _aiGenerator = aiGenerator;
        _settings = settings;
        _learningStore = learningStore;
        _reviewScheduler = reviewScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SentencePracticeSession> CreateSessionAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, MaxSessionSize);
        var aiEnabled = _settings.Current.AiEnabled;

        // 优先使用后台已经生成并持久化的例句。
        var generated = await _generatedSentences
            .GetRandomAsync(take, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<SentencePracticeItem>(take);
        var usedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sentence in generated)
        {
            if (sentence.Options is not { Count: >= 4 }
                || string.IsNullOrWhiteSpace(sentence.Word)
                || string.IsNullOrWhiteSpace(sentence.TranslationZh)
                || sentence.TranslationZh.Contains('_')
                || !usedWords.Add(sentence.Word))
            {
                continue;
            }

            items.Add(new SentencePracticeItem(
                sentence.WordId,
                sentence.Word,
                sentence.Sentence,
                sentence.Answer,
                sentence.AlternativeAnswers ?? [],
                sentence.TranslationZh,
                sentence.Pattern,
                sentence.GrammarNote,
                sentence.Options,
                FromAi: true));

            if (items.Count >= take)
            {
                break;
            }
        }

        var generatedCount = items.Count;
        var remaining = take - generatedCount;

        var entries = remaining > 0
            ? _sentenceBank.Entries
                .Where(x => !usedWords.Contains(x.Word))
                .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.ElementAt(Random.Shared.Next(group.Count())))
                .OrderBy(_ => Random.Shared.Next())
                .Take(remaining)
                .ToList()
            : [];

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

        if (items.Count == 0)
        {
            return new SentencePracticeSession([], aiEnabled, AiUsed: false, AiMessage: null);
        }

        // 已经有后台生成的句子：直接使用，不再发起在线 AI 请求，避免重复消耗。
        if (generatedCount > 0)
        {
            return new SentencePracticeSession(
                items,
                aiEnabled,
                AiUsed: true,
                AiMessage: null);
        }

        if (!aiEnabled)
        {
            return new SentencePracticeSession(items, AiEnabled: false, AiUsed: false, AiMessage: null);
        }

        if (cards.Count == 0)
        {
            return new SentencePracticeSession(items, AiEnabled: true, AiUsed: false, AiMessage: null);
        }

        var sentenceModel = SentenceModelHelper.ResolveModel(_settings.Current);

        if (!_settings.Current.AiAutoMode
            && SentenceModelHelper.IsReasoningModel(sentenceModel))
        {
            return new SentencePracticeSession(
                items,
                AiEnabled: true,
                AiUsed: false,
                AiMessage: $"当前句子模型是推理模型（{sentenceModel}），批量句子生成较慢；已使用离线句库。可在设置中把「句子练习模型」改为 glm-4-flash。");
        }

        var now = _timeProvider.GetUtcNow();
        if (IsAiCoolingDown(now))
        {
            return new SentencePracticeSession(items, AiEnabled: true, AiUsed: false, AiCooldownMessage);
        }

        try
        {
            var aiItems = await _aiGenerator
                .GenerateAsync(cards, cancellationToken)
                .ConfigureAwait(false);

            if (aiItems.Count == 0)
            {
                BeginAiCooldown(_timeProvider.GetUtcNow());
                return new SentencePracticeSession(items, AiEnabled: true, AiUsed: false, AiFallbackMessage);
            }

            var aiByWord = aiItems
                .Where(x => x.Options.Count >= 4)
                .GroupBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var merged = items
                .Select(item => aiByWord.TryGetValue(item.Word, out var ai) ? ai : item)
                .ToList();

            var aiCount = merged.Count(x => x.FromAi);
            if (aiCount == 0)
            {
                BeginAiCooldown(_timeProvider.GetUtcNow());
                return new SentencePracticeSession(items, AiEnabled: true, AiUsed: false, AiFallbackMessage);
            }

            ClearAiCooldown();
            _logger.LogInformation(
                "句子练习：{Total} 句，其中 AI 生成 {AiCount} 句。",
                merged.Count,
                aiCount);

            await PersistAiItemsAsync(
                    merged.Where(x => x.FromAi),
                    cancellationToken)
                .ConfigureAwait(false);

            return new SentencePracticeSession(merged, AiEnabled: true, AiUsed: true, AiMessage: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 生成句子失败，使用离线句库。");
            BeginAiCooldown(_timeProvider.GetUtcNow());
            return new SentencePracticeSession(items, AiEnabled: true, AiUsed: false, AiFallbackMessage);
        }
    }

    private async Task PersistAiItemsAsync(
        IEnumerable<SentencePracticeItem> items,
        CancellationToken cancellationToken)
    {
        var model = SentenceModelHelper.ResolveModel(_settings.Current);
        var now = _timeProvider.GetUtcNow();

        var generated = items
            .Where(x => x.Options.Count >= 4)
            .Select(x => new GeneratedSentence(
                x.WordId,
                x.Word,
                x.Sentence,
                x.Answer,
                x.AlternativeAnswers,
                x.TranslationZh,
                x.Pattern,
                x.GrammarNote,
                x.Options,
                model,
                now))
            .ToList();

        if (generated.Count == 0)
        {
            return;
        }

        try
        {
            await _generatedSentences
                .AddAsync(generated, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存在线生成的例句到本地库失败。");
        }
    }

    private bool IsAiCoolingDown(DateTimeOffset now)
    {
        lock (_aiGate)
        {
            return _aiCooldownUntil is { } until && now < until;
        }
    }

    private void BeginAiCooldown(DateTimeOffset now)
    {
        lock (_aiGate)
        {
            _aiCooldownUntil = now + AiFailureCooldown;
        }
    }

    private void ClearAiCooldown()
    {
        lock (_aiGate)
        {
            _aiCooldownUntil = null;
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
