using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Infrastructure.Ai;

namespace VocabularyCoach.Infrastructure.Tests.Ai;

public class OpenAiCompatibleEnrichmentProviderTests
{
    [Fact]
    public async Task EnrichAsync_SendsCompatibleRequest_AndParsesContent()
    {
        var handler = new StubHandler(
            """{"choices":[{"message":{"content":"{\"exampleEn\":\"A.\",\"exampleZh\":\"甲。\"}"}}]}""");
        var provider = new OpenAiCompatibleEnrichmentProvider(
            new HttpClient(handler),
            NullLogger<OpenAiCompatibleEnrichmentProvider>.Instance);

        var result = await provider.EnrichAsync(
            new AiEnrichmentRequest
            {
                Word = "resilient",
                DefinitionZh = "有韧性的",
                Difficulty = 3,
                BaseUrl = "https://mock.example/v1/",
                Model = "test-model",
                ApiKey = "sk-test",
            },
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("A.", result!.ExampleEn);
        Assert.Equal("甲。", result.ExampleZh);

        Assert.Equal("https://mock.example/v1/chat/completions", handler.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.Authorization.Parameter);
        Assert.Contains("resilient", handler.Body);
        Assert.Contains("test-model", handler.Body);
    }

    [Fact]
    public async Task EnrichAsync_ReturnsNull_OnHttpError()
    {
        var handler = new StubHandler("{}", HttpStatusCode.TooManyRequests);
        var provider = new OpenAiCompatibleEnrichmentProvider(
            new HttpClient(handler),
            NullLogger<OpenAiCompatibleEnrichmentProvider>.Instance);

        var result = await provider.EnrichAsync(
            new AiEnrichmentRequest
            {
                Word = "word",
                BaseUrl = "https://mock.example/v1",
                Model = "m",
                ApiKey = "sk-test",
            },
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task EnrichAsync_ReturnsNull_WithoutBaseUrlOrModel()
    {
        var provider = new OpenAiCompatibleEnrichmentProvider(
            new HttpClient(new StubHandler("{}")),
            NullLogger<OpenAiCompatibleEnrichmentProvider>.Instance);

        Assert.Null(await provider.EnrichAsync(
            new AiEnrichmentRequest { Word = "word", BaseUrl = "", Model = "m" },
            CancellationToken.None));

        Assert.Null(await provider.EnrichAsync(
            new AiEnrichmentRequest { Word = "word", BaseUrl = "https://x", Model = "" },
            CancellationToken.None));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _status;

        public StubHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
