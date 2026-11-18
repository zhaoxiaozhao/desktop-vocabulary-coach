using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Tests.Support;

namespace VocabularyCoach.Application.Tests.Settings;

public class AiConnectionSettingsTests
{
    [Fact]
    public void Ensure_MigratesLegacySingleConnection()
    {
        var settings = new UserSettings
        {
            AiPreset = AiPreset.DeepSeek,
            AiBaseUrl = "https://api.deepseek.com/v1",
            AiModel = "deepseek-chat",
            SentencePracticeModel = "deepseek-chat",
        };

        AiConnectionSettings.Ensure(settings);

        var connection = Assert.Single(settings.AiConnections);
        Assert.Equal(AiConnectionSettings.DefaultConnectionId, connection.Id);
        Assert.Equal(AiPreset.DeepSeek, connection.Preset);
        Assert.Equal("deepseek-chat", connection.Model);
        Assert.Equal(connection.Id, settings.ActiveAiConnectionId);
    }

    [Fact]
    public async Task Resolver_ManualUsesActiveConnection_AndAutoUsesEnabledOrder()
    {
        var settings = new UserSettings
        {
            AiEnabled = true,
            AiAutoMode = false,
            ActiveAiConnectionId = "b",
            AiConnections =
            [
                new AiConnection
                {
                    Id = "a",
                    Name = "A",
                    Preset = AiPreset.DeepSeek,
                    BaseUrl = "https://api.deepseek.com/v1",
                    Model = "deepseek-chat",
                },
                new AiConnection
                {
                    Id = "b",
                    Name = "B",
                    Preset = AiPreset.Zhipu,
                    BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                    Model = "glm-4-flash",
                    SentencePracticeModel = "glm-4-flash",
                },
                new AiConnection
                {
                    Id = "c",
                    Name = "C",
                    Enabled = false,
                },
            ],
        };

        var keys = new FakeAiKeyStore();
        await keys.SetAsync("a", "sk-a");
        await keys.SetAsync("b", "zhipu-key");
        var resolver = new AiConnectionResolver(keys);

        var manual = await resolver.GetCandidatesAsync(settings);
        var manualContext = Assert.Single(manual);
        Assert.Equal("b", manualContext.Connection.Id);
        Assert.Equal("zhipu-key", manualContext.ApiKey);

        settings.AiAutoMode = true;
        var auto = await resolver.GetCandidatesAsync(settings);
        Assert.Equal(["a", "b"], auto.Select(x => x.Connection.Id));
        Assert.Equal("sk-a", auto[0].ApiKey);
    }
}
