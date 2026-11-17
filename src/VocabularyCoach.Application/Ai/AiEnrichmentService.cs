using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Ai;

/// <summary>
/// AI 增强编排：缓存优先、后台生成、失败静默降级。
/// 不把用户学习记录发送给 AI，只发送当前单词与本地中文释义。
/// </summary>
public sealed class AiEnrichmentService : IAiEnrichmentService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly ISettingsService _settings;
    private readonly IAiCacheStore _cache;
    private readonly ISecretStore _secrets;
    private readonly IReadOnlyList<IVocabularyEnrichmentProvider> _providers;
    private readonly ILogger<AiEnrichmentService> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public AiEnrichmentService(
        ISettingsService settings,
        IAiCacheStore cache,
        ISecretStore secrets,
        IEnumerable<IVocabularyEnrichmentProvider> providers,
        ILogger<AiEnrichmentService> logger)
    {
        _settings = settings;
        _cache = cache;
        _secrets = secrets;
        _providers = providers.ToList();
        _logger = logger;
    }

    public async Task<WordEnrichment?> GetCachedAsync(int wordId, CancellationToken cancellationToken)
    {
        if (wordId <= 0 || !_settings.Current.AiEnabled)
        {
            return null;
        }

        try
        {
            return await _cache
                .GetAsync(wordId, _settings.Current.AiPreset.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取 AI 缓存失败：{WordId}", wordId);
            return null;
        }
    }

    public void QueueEnrichment(WordCard card)
    {
        var settings = _settings.Current;
        if (!settings.AiEnabled || card.Id <= 0)
        {
            return;
        }

        if (!_pending.TryAdd(card.Id, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (await GetCachedAsync(card.Id, CancellationToken.None).ConfigureAwait(false) is not null)
                {
                    return;
                }

                var apiKey = await _secrets.TryReadAsync(CancellationToken.None).ConfigureAwait(false) ?? string.Empty;
                await GenerateAsync(card, apiKey, cacheResult: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI 增强失败（不影响学习功能）：{Word}", card.Word);
            }
            finally
            {
                _pending.TryRemove(card.Id, out _);
            }
        });
    }

    public async Task<WordEnrichment?> EnrichNowAsync(WordCard card, CancellationToken cancellationToken)
    {
        var apiKey = await _secrets.TryReadAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        return await GenerateAsync(card, apiKey, cacheResult: card.Id > 0, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiTestResult> TestAsync(string? apiKeyOverride, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var provider = Resolve(settings.AiPreset);

        if (provider is null)
        {
            return new AiTestResult(false, "未找到对应的 AI Provider 实现。");
        }

        if (provider.Kind == "Mock")
        {
            return new AiTestResult(true, "Mock Provider 可用（离线示例，无需网络与 Key）。");
        }

        if (string.IsNullOrWhiteSpace(settings.AiBaseUrl) || string.IsNullOrWhiteSpace(settings.AiModel))
        {
            return new AiTestResult(false, "请填写 Base URL 与模型名称。");
        }

        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? await _secrets.TryReadAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty
            : apiKeyOverride;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiTestResult(false, "尚未配置 API Key。");
        }

        var card = new WordCard
        {
            Word = "resilient",
            Difficulty = 2,
            Definitions = [new WordDefinition { DefinitionZh = "有韧性的；能迅速恢复的" }],
        };

        var result = await GenerateAsync(card, apiKey, cacheResult: false, cancellationToken).ConfigureAwait(false);

        return result is null
            ? new AiTestResult(
                false,
                "连接失败：请检查 Base URL、模型名称与 API Key。"
                + "常见原因：Key 与服务商不匹配、Base URL/模型名错误、余额不足，"
                + "或免费模型只允许官方客户端使用。日志中有 HTTP 状态码。")
            : new AiTestResult(true, $"连接成功：模型返回了有效内容（{settings.AiModel}）。");
    }

    private async Task<WordEnrichment?> GenerateAsync(
        WordCard card,
        string apiKey,
        bool cacheResult,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (!settings.AiEnabled)
        {
            return null;
        }

        var provider = Resolve(settings.AiPreset);
        if (provider is null)
        {
            return null;
        }

        var isMock = provider.Kind == "Mock";
        if (!isMock && string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("AI 已启用但未配置 API Key，跳过增强。");
            return null;
        }

        if (!isMock &&
            (string.IsNullOrWhiteSpace(settings.AiBaseUrl) || string.IsNullOrWhiteSpace(settings.AiModel)))
        {
            _logger.LogDebug("AI Base URL 或模型未配置，跳过增强。");
            return null;
        }

        var request = new AiEnrichmentRequest
        {
            Word = card.Word,
            DefinitionZh = card.PrimaryDefinition?.DefinitionZh,
            Difficulty = card.Difficulty,
            BaseUrl = settings.AiBaseUrl,
            Model = settings.AiModel,
            ApiKey = apiKey,
        };

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            var enrichment = await provider.EnrichAsync(request, timeout.Token).ConfigureAwait(false);
            if (enrichment is null)
            {
                _logger.LogWarning("AI 返回内容无法解析或请求失败：{Word}", card.Word);
                return null;
            }

            if (cacheResult && card.Id > 0)
            {
                try
                {
                    await _cache
                        .SaveAsync(
                            card.Id,
                            settings.AiPreset.ToString(),
                            settings.AiModel,
                            enrichment,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "保存 AI 缓存失败：{Word}", card.Word);
                }
            }

            return enrichment;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AI 请求超时（{Seconds} 秒）：{Word}", RequestTimeout.TotalSeconds, card.Word);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 请求失败：{Word}", card.Word);
            return null;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private IVocabularyEnrichmentProvider? Resolve(AiPreset preset)
    {
        var kind = preset == AiPreset.Mock ? "Mock" : "OpenAICompatible";
        return _providers.FirstOrDefault(x => string.Equals(x.Kind, kind, StringComparison.Ordinal));
    }
}
