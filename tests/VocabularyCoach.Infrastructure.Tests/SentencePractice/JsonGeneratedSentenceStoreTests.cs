using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.SentencePractice;
using VocabularyCoach.Infrastructure.SentencePractice;

namespace VocabularyCoach.Infrastructure.Tests.SentencePractice;

public class JsonGeneratedSentenceStoreTests
{
    [Fact]
    public async Task AddAndReload_DeduplicatesAndCounts()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dvc-generated-sentences-{Guid.NewGuid():N}.json");

        try
        {
            var store = new JsonGeneratedSentenceStore(
                path,
                NullLogger<JsonGeneratedSentenceStore>.Instance);

            var sentence = new GeneratedSentence(
                1,
                "favor",
                "Could you do me a ____?",
                "favor",
                [],
                "你能帮我个忙吗？",
                "do sb a favor",
                "favor 作名词。",
                ["favor", "case", "matter", "problem"],
                "glm-4-flash",
                DateTimeOffset.UtcNow);

            await store.AddAsync([sentence, sentence]);
            await store.AddAsync([sentence with { Sentence = "Can I ask you a ____?" }]);
            await store.AddAsync(
            [
                sentence with
                {
                    Sentence = "Bad ____ translation.",
                    TranslationZh = "坏的__翻译。",
                },
            ]);

            Assert.Equal(2, await store.CountForWordAsync(1));
            Assert.Equal(2, await store.CountSinceAsync(DateTimeOffset.UtcNow.AddDays(-1)));
            Assert.Equal(2, (await store.GetRandomAsync(10)).Count);

            var reloaded = new JsonGeneratedSentenceStore(
                path,
                NullLogger<JsonGeneratedSentenceStore>.Instance);

            Assert.Equal(2, await reloaded.CountForWordAsync(1));
            var random = await reloaded.GetRandomForWordAsync(1);
            Assert.NotNull(random);
            Assert.Contains("____", random!.Sentence);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
