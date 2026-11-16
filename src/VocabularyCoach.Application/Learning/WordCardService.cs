using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;
using VocabularyCoach.Core.Extensions;

namespace VocabularyCoach.Application.Learning;

/// <summary>
/// 卡片展示的统一入口：取词、展示、记录结果并按 SM-2 计算下一次复习。
/// 同一次最多只允许一张卡片，避免调度器和手动触发并发弹窗。
/// </summary>
public sealed class WordCardService
{
    private readonly IWordCardSource _wordCardSource;
    private readonly IWordPopupPresenter _popupPresenter;
    private readonly ILearningStore _learningStore;
    private readonly IReviewScheduler _reviewScheduler;
    private readonly IAdaptiveDifficultyService _adaptiveDifficulty;
    private readonly IAiEnrichmentService _aiEnrichment;
    private readonly ILicenseService _license;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WordCardService> _logger;
    private readonly SemaphoreSlim _singleCardGate = new(1, 1);

    public WordCardService(
        IWordCardSource wordCardSource,
        IWordPopupPresenter popupPresenter,
        ILearningStore learningStore,
        IReviewScheduler reviewScheduler,
        IAdaptiveDifficultyService adaptiveDifficulty,
        IAiEnrichmentService aiEnrichment,
        ILicenseService license,
        TimeProvider timeProvider,
        ILogger<WordCardService> logger)
    {
        _wordCardSource = wordCardSource;
        _popupPresenter = popupPresenter;
        _learningStore = learningStore;
        _reviewScheduler = reviewScheduler;
        _adaptiveDifficulty = adaptiveDifficulty;
        _aiEnrichment = aiEnrichment;
        _license = license;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>立即展示下一张卡片（来源默认为手动）。</summary>
    public Task<WordCardResult?> ShowNextCardAsync(CancellationToken cancellationToken = default) =>
        ShowNextCardAsync(LearningSource.Manual, cancellationToken);

    /// <summary>立即展示下一张卡片；没有可展示的词时返回 null。</summary>
    /// <exception cref="InvalidOperationException">当前已有卡片正在展示。</exception>
    public async Task<WordCardResult?> ShowNextCardAsync(
        LearningSource source,
        CancellationToken cancellationToken = default)
    {
        if (!await _singleCardGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("已有单词卡片正在显示，不能重复弹出。");
        }

        try
        {
            if (!_license.CanUsePremiumFeatures)
            {
                _logger.LogInformation("试用已结束或未激活，跳过卡片展示。");
                return null;
            }

            var card = await _wordCardSource.GetNextCardAsync(cancellationToken).ConfigureAwait(false);
            if (card is null)
            {
                _logger.LogInformation("词库中没有可展示的单词。");
                return null;
            }

            card = await ApplyAiEnrichmentAsync(card, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("展示单词卡片：{Word}（来源 {Source}）", card.Word, source);

            var shownAt = _timeProvider.GetUtcNow();
            var result = await _popupPresenter.ShowAsync(card, cancellationToken).ConfigureAwait(false);
            var respondedAt = _timeProvider.GetUtcNow();

            var schedule = await SaveOutcomeAsync(card, source, shownAt, respondedAt, result)
                .ConfigureAwait(false);

            if (schedule is not null)
            {
                try
                {
                    await _adaptiveDifficulty.ApplyAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "自适应难度更新失败：{Word}", card.Word);
                }
            }

            _logger.LogInformation(
                "单词卡片结束：{Word}，结果 {Result}，有效反馈 {Feedback}，下次复习 {NextDue:u}",
                card.Word,
                result,
                result.ToFeedbackOrNull()?.ToString() ?? "无",
                schedule?.DueAt);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("卡片展示被取消。");
            throw;
        }
        finally
        {
            _singleCardGate.Release();
        }
    }

    private async Task<WordCard> ApplyAiEnrichmentAsync(WordCard card, CancellationToken cancellationToken)
    {
        if (card.Id <= 0)
        {
            return card;
        }

        try
        {
            var enrichment = await _aiEnrichment.GetCachedAsync(card.Id, cancellationToken).ConfigureAwait(false);
            if (enrichment is not null)
            {
                return card with { Enrichment = enrichment };
            }

            // 没有缓存时不阻塞卡片，后台生成，下一次出现该词时使用。
            _aiEnrichment.QueueEnrichment(card);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取 AI 增强失败：{Word}", card.Word);
        }

        return card;
    }

    private async Task<ReviewScheduleResult?> SaveOutcomeAsync(
        WordCard card,
        LearningSource source,
        DateTimeOffset shownAt,
        DateTimeOffset respondedAt,
        WordCardResult result)
    {
        if (card.Id <= 0)
        {
            // 未持久化的卡片（测试或内存数据）不写学习记录。
            return null;
        }

        var feedback = result.ToFeedbackOrNull();
        ReviewState? state = null;
        ReviewScheduleResult? schedule = null;

        if (feedback is { } value)
        {
            try
            {
                // 反馈一旦产生就必须落库，不能因退出取消令牌而丢失。
                state = await _learningStore.GetReviewStateAsync(card.Id, CancellationToken.None).ConfigureAwait(false);
                schedule = _reviewScheduler.Schedule(state ?? ReviewState.New, value.ToReviewRating(), respondedAt);
            }
            catch (Exception ex)
            {
                // 读取进度失败时不写入新进度，避免覆盖已有学习记录；会话仍会保存。
                _logger.LogError(ex, "读取复习状态失败，跳过本次进度更新：{Word}", card.Word);
            }
        }

        var durationMs = Math.Max(0, (int)(respondedAt - shownAt).TotalMilliseconds);
        var outcome = new CardOutcome(
            card.Id,
            source,
            shownAt,
            respondedAt,
            durationMs,
            result,
            feedback,
            schedule,
            state?.DueAt);

        try
        {
            await _learningStore.SaveCardOutcomeAsync(outcome, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存学习记录失败：{Word}", card.Word);
        }

        return schedule;
    }
}
