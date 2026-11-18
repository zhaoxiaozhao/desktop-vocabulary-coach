using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;

namespace VocabularyCoach.Application.Ai;

/// <summary>
/// AI 增强编排：缓存优先、后台生成、失败静默降级。
/// 支持手动连接与 Auto 多连接回退。
/// </summary>
public sealed class AiEnrichmentService : IAiEnrichmentService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly ISettingsService _settings;
    private readonly IAiCacheStore _cache;
    private readonly IAiKeyStore _keys;
    private readonly IAiConnectionResolver _resolver;
    private readonly IReadOnlyList<IVocabularyEnrichmentProvider> _providers;
    private readonly ILogger<AiEnrichmentService> _logger;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public AiEnrichmentService(
        ISettingsService settings,
        IAiCacheStore cache,
        IAiKeyStore keys,
        IAiConnectionResolver resolver,
        IEnumerable<IVocabularyEnrichmentProvider> providers,
        ILogger<AiEnrichmentService> logger)
    {
        _settings = settings;
        _cache = cache;
        _keys = keys;
        _resolver = resolver;
        _providers = providers.ToList();
        _logger = logger;
    }

    public async Task<WordEnrichment?> GetCachedAsync(int wordId, CancellationToken cancellationToken)
    {
        if (wordId <= 0 || !_settings.Current.AiEnabled)
        {
            return null;
        }

        AiConnectionSettings.Ensure(_settings.Current);
        var presetKeys = _settings.Current.AiConnections
            .Where(x => x.Enabled)
            .Select(x => x.Preset.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (presetKeys.Count == 0)
        {
            presetKeys.Add(_settings.Current.AiPreset.ToString());
        }

        foreach (var preset in presetKeys)
        {
            try
            {
                var cached = await _cache
                    .GetAsync(wordId, preset, cancellationToken)
                    .ConfigureAwait(false);

                if (cached is not null)
                {
                    return cached;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取 AI 缓存失败：{WordId}", wordId);
            }
        }

        return null;
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

                await GenerateAsync(card, apiKeyOverride: null, cacheResult: true, CancellationToken.None)
                    .ConfigureAwait(false);
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

    public Task<WordEnrichment?> EnrichNowAsync(WordCard card, CancellationToken cancellationToken) =>
        GenerateAsync(card, apiKeyOverride: null, cacheResult: card.Id > 0, cancellationToken);

    /// <summary>测试当前手动选中的连接（兼容旧调用）。</summary>
    public Task<AiTestResult> TestAsync(
        string? apiKeyOverride,
        CancellationToken cancellationToken)
    {
        var active = AiConnectionSettings.GetActive(_settings.Current)
            ?? new AiConnection
            {
                Name = "默认连接",
                Preset = _settings.Current.AiPreset,
                BaseUrl = _settings.Current.AiBaseUrl,
                Model = _settings.Current.AiModel,
            };

        return TestConnectionAsync(active, apiKeyOverride, cancellationToken);
    }

    /// <summary>测试设置页当前编辑的连接（尚未保存也可以测试）。</summary>
    public Task<AiTestResult> TestAsync(
        string? apiKeyOverride,
        AiConnection connection,
        CancellationToken cancellationToken) =>
        TestConnectionAsync(connection, apiKeyOverride, cancellationToken);

    private async Task<AiTestResult> TestConnectionAsync(
        AiConnection connection,
        string? apiKeyOverride,
        CancellationToken cancellationToken)
    {
        var provider = Resolve(connection.Preset);
        if (provider is null)
        {
            return new AiTestResult(false, "未找到对应的 AI Provider 实现。");
        }

        if (provider.Kind == "Mock")
        {
            return new AiTestResult(true, "Mock Provider 可用（离线示例，无需网络与 Key）。");
        }

        if (string.IsNullOrWhiteSpace(connection.BaseUrl)
            || string.IsNullOrWhiteSpace(connection.Model))
        {
            return new AiTestResult(false, "请填写 Base URL 与模型名称。");
        }

        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? await _keys.GetAsync(connection.Id, cancellationToken).ConfigureAwait(false) ?? string.Empty
            : apiKeyOverride;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiTestResult(false, "尚未配置 API Key。");
        }

        var mismatch = DescribeKeyMismatch(connection.BaseUrl, apiKey);
        if (mismatch is not null)
        {
            return new AiTestResult(false, mismatch);
        }

        var card = new WordCard
        {
            Word = "resilient",
            Difficulty = 2,
            Definitions = [new WordDefinition { DefinitionZh = "有韧性的；能迅速恢复的" }],
        };

        var request = new AiEnrichmentRequest
        {
            Word = card.Word,
            DefinitionZh = card.PrimaryDefinition?.DefinitionZh,
            Difficulty = card.Difficulty,
            BaseUrl = connection.BaseUrl,
            Model = connection.Model,
            ApiKey = apiKey,
        };

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            var result = await provider.EnrichAsync(request, timeout.Token).ConfigureAwait(false);
            return result is null
                ? new AiTestResult(
                    false,
                    "连接失败：请检查 Base URL、模型名称与 API Key。"
                    + "常见原因：Key 与服务商不匹配、Base URL/模型名错误、余额不足，"
                    + "或免费模型只允许官方客户端使用。日志中有 HTTP 状态码。")
                : new AiTestResult(
                    true,
                    $"连接成功：{connection.Name} 返回了有效内容（{connection.Model}）。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiTestResult(false, "连接超时，请检查网络或换一个模型。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "测试 AI 连接失败。");
            return new AiTestResult(false, "连接失败，详细信息已写入日志。");
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static string? DescribeKeyMismatch(string baseUrl, string apiKey)
    {
        var url = baseUrl.ToLowerInvariant();

        if (url.Contains("bigmodel.cn")
            && apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            return "当前 Base URL 是智谱 GLM，但 API Key 以 sk- 开头（DeepSeek/OpenAI 格式）。"
                + "请把服务商切换为 DeepSeek，Base URL 改为 https://api.deepseek.com/v1，模型改为 deepseek-chat。";
        }

        if (url.Contains("deepseek.com")
            && !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            return "当前 Base URL 是 DeepSeek，但 API Key 不是 sk- 格式。"
                + "请确认粘贴的是 DeepSeek 平台创建的 API Key。";
        }

        if (apiKey.StartsWith("oc_", StringComparison.OrdinalIgnoreCase)
            && !url.Contains("opencode.ai"))
        {
            return "这个 Key 是 OpenCode 格式（oc_），但当前 Base URL 不是 OpenCode。"
                + "请把服务商/Base URL 切换为 OpenCode，或换成对应服务商的 Key。";
        }

        return null;
    }

    private async Task<WordEnrichment?> GenerateAsync(
        WordCard card,
        string? apiKeyOverride,
        bool cacheResult,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (!settings.AiEnabled)
        {
            return null;
        }

        var candidates = await _resolver
            .GetCandidatesAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            _logger.LogDebug("AI 已启用但没有可用连接。");
            return null;
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var provider = Resolve(candidate.Connection.Preset);
                if (provider is null)
                {
                    continue;
                }

                var isMock = provider.Kind == "Mock";
                var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
                    ? candidate.ApiKey
                    : apiKeyOverride;

                if (!isMock && string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogDebug(
                        "连接 {Name} 未配置 API Key，跳过。",
                        candidate.Connection.Name);
                    continue;
                }

                if (!isMock
                    && (string.IsNullOrWhiteSpace(candidate.Connection.BaseUrl)
                        || string.IsNullOrWhiteSpace(candidate.Connection.Model)))
                {
                    _logger.LogDebug(
                        "连接 {Name} 未配置 Base URL/模型，跳过。",
                        candidate.Connection.Name);
                    continue;
                }

                var request = new AiEnrichmentRequest
                {
                    Word = card.Word,
                    DefinitionZh = card.PrimaryDefinition?.DefinitionZh,
                    Difficulty = card.Difficulty,
                    BaseUrl = candidate.Connection.BaseUrl,
                    Model = candidate.Connection.Model,
                    ApiKey = apiKey,
                };

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RequestTimeout);

                    var enrichment = await provider
                        .EnrichAsync(request, timeout.Token)
                        .ConfigureAwait(false);

                    if (enrichment is null)
                    {
                        _logger.LogWarning(
                            "连接 {Name} 未返回有效内容：{Word}",
                            candidate.Connection.Name,
                            card.Word);

                        if (!settings.AiAutoMode)
                        {
                            return null;
                        }

                        continue;
                    }

                    if (cacheResult && card.Id > 0)
                    {
                        try
                        {
                            await _cache
                                .SaveAsync(
                                    card.Id,
                                    candidate.Connection.Preset.ToString(),
                                    candidate.Connection.Model,
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
                    _logger.LogWarning(
                        "连接 {Name} 请求超时（{Seconds} 秒）：{Word}",
                        candidate.Connection.Name,
                        RequestTimeout.TotalSeconds,
                        card.Word);

                    if (!settings.AiAutoMode)
                    {
                        return null;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "连接 {Name} 请求失败：{Word}",
                        candidate.Connection.Name,
                        card.Word);

                    if (!settings.AiAutoMode)
                    {
                        return null;
                    }
                }
            }

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
