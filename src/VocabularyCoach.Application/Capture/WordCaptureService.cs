using VocabularyCoach.Application.Abstractions;

namespace VocabularyCoach.Application.Capture;

/// <summary>
/// 捕获编排：解析 → 查词 → 入库 → 记录到生词本。
/// 捕获的词会立即到期，进入现有复习队列。
/// </summary>
public sealed class WordCaptureService : IWordCaptureService
{
    private readonly IWordLibrary _wordLibrary;
    private readonly IWordCaptureStore _captureStore;

    public WordCaptureService(IWordLibrary wordLibrary, IWordCaptureStore captureStore)
    {
        _wordLibrary = wordLibrary;
        _captureStore = captureStore;
    }

    public async Task<CapturePreview> InspectAsync(string rawText, CancellationToken cancellationToken)
    {
        if (!WordTextParser.TryParse(rawText, out var parsed, out var error))
        {
            return new CapturePreview(
                IsValid: false,
                Error: error,
                OriginalText: rawText?.Trim() ?? string.Empty,
                NormalizedWord: string.Empty,
                ExistsInLibrary: false,
                AlreadyLearning: false,
                Card: null,
                SuggestedDefinition: null);
        }

        var card = await _wordLibrary.FindByWordAsync(parsed.Normalized, cancellationToken).ConfigureAwait(false);
        var alreadyLearning = card is not null &&
                              await _captureStore.HasProgressAsync(card.Id, cancellationToken).ConfigureAwait(false);

        return new CapturePreview(
            IsValid: true,
            Error: null,
            OriginalText: parsed.Original,
            NormalizedWord: parsed.Normalized,
            ExistsInLibrary: card is not null,
            AlreadyLearning: alreadyLearning,
            Card: card,
            SuggestedDefinition: card?.PrimaryDefinition?.DefinitionZh);
    }

    public async Task<CaptureResult> AddAsync(
        string rawText,
        string? definitionZh,
        CancellationToken cancellationToken)
    {
        if (!WordTextParser.TryParse(rawText, out var parsed, out var error))
        {
            return new CaptureResult(CaptureOutcome.Invalid, rawText?.Trim() ?? string.Empty, error);
        }

        var card = await _wordLibrary.FindByWordAsync(parsed.Normalized, cancellationToken).ConfigureAwait(false);

        if (card is not null)
        {
            if (await _captureStore.HasProgressAsync(card.Id, cancellationToken).ConfigureAwait(false))
            {
                await _captureStore
                    .RecordCaptureAsync(parsed.Original, parsed.Normalized, card.Id, cancellationToken)
                    .ConfigureAwait(false);

                return new CaptureResult(
                    CaptureOutcome.AlreadyLearning,
                    parsed.Original,
                    "该单词已在生词本或复习计划中。");
            }

            await _captureStore.EnsureDueNowAsync(card.Id, cancellationToken).ConfigureAwait(false);
            await _captureStore
                .RecordCaptureAsync(parsed.Original, parsed.Normalized, card.Id, cancellationToken)
                .ConfigureAwait(false);

            return new CaptureResult(CaptureOutcome.Added, parsed.Original, "已加入生词本，并安排在最近复习。");
        }

        if (string.IsNullOrWhiteSpace(definitionZh))
        {
            return new CaptureResult(
                CaptureOutcome.NeedsDefinition,
                parsed.Original,
                "词库中没有这个单词/短语，请填写中文释义后再加入。");
        }

        var wordId = await _captureStore
            .CreateWordWithProgressAsync(
                parsed.Original,
                parsed.Normalized,
                definitionZh.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        await _captureStore
            .RecordCaptureAsync(parsed.Original, parsed.Normalized, wordId, cancellationToken)
            .ConfigureAwait(false);

        return new CaptureResult(CaptureOutcome.Added, parsed.Original, "已加入生词本，并安排在最近复习。");
    }

    public Task<IReadOnlyList<CapturedWord>> GetCapturedAsync(
        int limit,
        CancellationToken cancellationToken) =>
        _captureStore.GetCapturedAsync(Math.Clamp(limit, 1, 2000), cancellationToken);
}
