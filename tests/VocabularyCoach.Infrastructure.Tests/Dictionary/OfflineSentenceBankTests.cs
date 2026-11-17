using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Infrastructure.Dictionary;

namespace VocabularyCoach.Infrastructure.Tests.Dictionary;

public class OfflineSentenceBankTests
{
    [Fact]
    public void Load_SkipsInvalidEntries()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dvc-sentences-{Guid.NewGuid():N}.json");

        File.WriteAllText(path, """
            {
              "version": 1,
              "sentences": [
                {
                  "word": "favor",
                  "sentence": "Could you do me a ____?",
                  "answer": "favor",
                  "translationZh": "你能帮我个忙吗？",
                  "distractors": ["case", "matter", "problem"]
                },
                {
                  "word": "broken",
                  "sentence": "This sentence has no blank.",
                  "answer": "broken",
                  "translationZh": "没有空格。",
                  "distractors": ["a", "b", "c"]
                },
                {
                  "word": "short",
                  "sentence": "Only ____ blank but not enough distractors.",
                  "answer": "short",
                  "translationZh": "干扰项不足。",
                  "distractors": ["a", "b"]
                }
              ]
            }
            """);

        try
        {
            var bank = new OfflineSentenceBank(path, NullLogger<OfflineSentenceBank>.Instance);

            var entry = Assert.Single(bank.Entries);
            Assert.Equal("favor", entry.Word);
            Assert.Equal("favor", entry.Answer);
            Assert.Equal(3, entry.Distractors.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
