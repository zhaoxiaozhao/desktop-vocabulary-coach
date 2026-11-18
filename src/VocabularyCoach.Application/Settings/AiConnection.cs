using VocabularyCoach.Application.Ai;

namespace VocabularyCoach.Application.Settings;

/// <summary>一个可独立配置的 AI 连接（服务商 + 地址 + 模型 + 独立 Key）。</summary>
public sealed class AiConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "新连接";

    public AiPreset Preset { get; set; } = AiPreset.OpenAi;

    public string BaseUrl { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>句子练习/后台生成使用的模型；留空则使用 Model。</summary>
    public string SentencePracticeModel { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public AiConnection Clone() => (AiConnection)MemberwiseClone();
}

/// <summary>设置页下拉框使用的连接选项。</summary>
public sealed record AiConnectionOption(string Id, string Name);

/// <summary>多连接配置的迁移与同步。</summary>
public static class AiConnectionSettings
{
    public const string DefaultConnectionId = "default";

    /// <summary>
    /// 确保至少有一个连接；旧版单连接字段自动迁移为「默认连接」。
    /// 同步旧的 AiPreset/AiBaseUrl/AiModel 为当前选中连接，兼容尚未改造的调用方。
    /// </summary>
    public static void Ensure(UserSettings settings)
    {
        settings.AiConnections ??= [];

        if (settings.AiConnections.Count == 0)
        {
            settings.AiConnections.Add(new AiConnection
            {
                Id = DefaultConnectionId,
                Name = "默认连接",
                Preset = settings.AiPreset,
                BaseUrl = settings.AiBaseUrl,
                Model = settings.AiModel,
                SentencePracticeModel = settings.SentencePracticeModel,
                Enabled = true,
            });
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in settings.AiConnections)
        {
            if (string.IsNullOrWhiteSpace(connection.Id) || !seen.Add(connection.Id))
            {
                connection.Id = Guid.NewGuid().ToString("N");
                seen.Add(connection.Id);
            }

            connection.Name = string.IsNullOrWhiteSpace(connection.Name)
                ? "未命名连接"
                : connection.Name.Trim();
            connection.BaseUrl = (connection.BaseUrl ?? string.Empty).Trim();
            connection.Model = (connection.Model ?? string.Empty).Trim();
            connection.SentencePracticeModel =
                (connection.SentencePracticeModel ?? string.Empty).Trim();
        }

        var active = settings.AiConnections
            .FirstOrDefault(x => string.Equals(
                x.Id,
                settings.ActiveAiConnectionId,
                StringComparison.OrdinalIgnoreCase));

        if (active is null)
        {
            active = settings.AiConnections.FirstOrDefault(x => x.Enabled)
                ?? settings.AiConnections[0];
            settings.ActiveAiConnectionId = active.Id;
        }

        SyncLegacy(settings, active);
    }

    /// <summary>把当前连接写回旧字段，供未感知多连接的调用方使用。</summary>
    public static void SyncLegacy(UserSettings settings, AiConnection active)
    {
        settings.AiPreset = active.Preset;
        settings.AiBaseUrl = active.BaseUrl;
        settings.AiModel = active.Model;
        settings.SentencePracticeModel = active.SentencePracticeModel;
    }

    public static AiConnection? GetActive(UserSettings settings)
    {
        Ensure(settings);
        return settings.AiConnections
            .FirstOrDefault(x => string.Equals(
                x.Id,
                settings.ActiveAiConnectionId,
                StringComparison.OrdinalIgnoreCase));
    }
}
