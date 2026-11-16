using System.Text.Json;
using VocabularyCoach.Application.Ai;

namespace VocabularyCoach.Application.Tests.Ai;

public class AiResponseParserTests
{
    [Fact]
    public void Parse_ReadsPlainJson()
    {
        var result = AiResponseParser.Parse(
            """{"exampleEn":"She is resilient.","exampleZh":"她很有韧性。","explanationZh":"recover quickly","confusableNote":null}""");

        Assert.NotNull(result);
        Assert.Equal("She is resilient.", result!.ExampleEn);
        Assert.Equal("她很有韧性。", result.ExampleZh);
        Assert.Equal("recover quickly", result.ExplanationZh);
        Assert.Null(result.ConfusableNote);
    }

    [Fact]
    public void Parse_StripsMarkdownFence()
    {
        var result = AiResponseParser.Parse(
            "```json\n{\"exampleEn\":\"A.\",\"exampleZh\":\"甲。\"}\n```");

        Assert.NotNull(result);
        Assert.Equal("A.", result!.ExampleEn);
    }

    [Fact]
    public void Parse_IgnoresSurroundingText()
    {
        var result = AiResponseParser.Parse(
            "Here is the result:\n{\"explanationZh\":\"简释\"}\nHope it helps.");

        Assert.NotNull(result);
        Assert.Equal("简释", result!.ExplanationZh);
    }

    [Fact]
    public void Parse_AcceptsSnakeCaseKeys()
    {
        var result = AiResponseParser.Parse(
            "{\"example_en\":\"A.\",\"example_zh\":\"甲。\"}");

        Assert.NotNull(result);
        Assert.Equal("A.", result!.ExampleEn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"exampleEn\":\"\"}")]
    public void Parse_ReturnsNullForInvalidContent(string? content)
    {
        Assert.Null(AiResponseParser.Parse(content));
    }

    [Fact]
    public void WordEnrichment_SerializesAndDeserializes()
    {
        var enrichment = new Core.Entities.WordEnrichment("A.", "甲。", "简释", "辨析");
        var json = JsonSerializer.Serialize(enrichment);

        var parsed = AiResponseParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal("A.", parsed!.ExampleEn);
        Assert.Equal("辨析", parsed.ConfusableNote);
    }
}
