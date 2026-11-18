using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Recommendation;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.SentencePractice;

/// <summary>
/// 电脑空闲时在后台调用 AI 生成例句，写入本地例句库。
/// 每天最多运行「组数」，每次生成「每组句数」；失败后冷却 30 分钟。
/// </summary>
public sealed class SentenceGenerationService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(30);

    private readonly ISettingsService _settings;
    private readonly ISystemStateMonitor _systemState;
    private readonly WordRecommendationService _recommendation;
    private readonly ISentencePracticeGenerator _generator;
    private readonly IGeneratedSentenceStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SentenceGenerationService> _logger;

    private DateTimeOffset? _lastFailureAt;
    private bool _reasoningModelLogged;

    public SentenceGenerationService(
        ISettingsService settings,
        ISystemStateMonitor systemState,
        WordRecommendationService recommendation,
        ISentencePracticeGenerator generator,
        IGeneratedSentenceStore store,
        TimeProvider timeProvider,
        ILogger<SentenceGenerationService> logger)
    {
        _settings = settings;
        _systemState = systemState;
        _recommendation = recommendation;
        _generator = generator;
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await GenerateOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台生成例句失败。");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>执行一次检查；返回是否写入了新句子（便于测试/手动触发）。</summary>
    public async Task<bool> GenerateOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        if (!settings.SentenceGenerationEnabled || !settings.AiEnabled)
        {
            return false;
        }

        var model = SentenceModelHelper.ResolveModel(settings);
        if (!settings.AiAutoMode && SentenceModelHelper.IsReasoningModel(model))
        {
            if (!_reasoningModelLogged)
            {
                _reasoningModelLogged = true;
                _logger.LogInformation(
                    "句子练习模型 {Model} 是推理模型，后台例句生成已跳过；建议在设置中填写 glm-4-flash。",
                    model);
            }

            return false;
        }

        var now = _timeProvider.GetUtcNow();
        if (_lastFailureAt is { } last && now - last < FailureCooldown)
        {
            return false;
        }

        var localToday = _timeProvider.GetLocalNow().Date;
        var localOffset = _timeProvider.LocalTimeZone.GetUtcOffset(localToday);
        var todayStartUtc = new DateTimeOffset(localToday, localOffset).ToUniversalTime();
        var generatedToday = await _store
            .CountSinceAsync(todayStartUtc, cancellationToken)
            .ConfigureAwait(false);

        var runs = Math.Clamp(settings.SentenceGenerationRunsPerDay, 1, 6);
        var batchSize = Math.Clamp(settings.SentenceGenerationBatchSize, 1, 10);
        if (generatedToday >= runs * batchSize)
        {
            return false;
        }

        var state = await _systemState
            .GetStateAsync(cancellationToken)
            .ConfigureAwait(false);

        if (state.DetectionFailed || state.IsFullscreenAppActive)
        {
            return false;
        }

        var idleMinutes = Math.Clamp(settings.SentenceGenerationIdleMinutes, 1, 120);
        if (state.IdleTime < TimeSpan.FromMinutes(idleMinutes))
        {
            return false;
        }

        var cards = await PickCardsAsync(batchSize, cancellationToken).ConfigureAwait(false);
        if (cards.Count == 0)
        {
            return false;
        }

        var items = await _generator
            .GenerateAsync(cards, cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            _lastFailureAt = now;
            _logger.LogWarning("后台生成例句没有返回有效内容，30 分钟后重试。");
            return false;
        }

        var sentences = items
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

        await _store.AddAsync(sentences, cancellationToken).ConfigureAwait(false);
        _lastFailureAt = null;

        _logger.LogInformation(
            "后台生成例句完成：{Count} 条（模型 {Model}，今日累计 {Today} 条）。",
            sentences.Count,
            model,
            generatedToday + sentences.Count);

        return sentences.Count > 0;
    }

    private async Task<List<WordCard>> PickCardsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        var cards = new List<WordCard>(count);
        var seen = new HashSet<int>();

        // 推荐服务可能反复返回同一张卡；限制尝试次数，避免死循环。
        for (var attempt = 0; attempt < count * 8 && cards.Count < count; attempt++)
        {
            var card = await _recommendation
                .GetNextCardAsync(cancellationToken)
                .ConfigureAwait(false);

            if (card is null || card.Id <= 0)
            {
                break;
            }

            if (!seen.Add(card.Id))
            {
                continue;
            }

            if (await _store
                    .CountForWordAsync(card.Id, cancellationToken)
                    .ConfigureAwait(false) >= 2)
            {
                continue;
            }

            cards.Add(card);
        }

        return cards;
    }
}
