using VocabularyCoach.Core.Entities;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Persistence.Mapping;

/// <summary>数据库实体到 Application/Core 模型的最小映射。</summary>
public static class WordCardMapper
{
    public static WordCard ToCard(WordRecord record) => new()
    {
        Id = record.Id,
        Word = record.Word,
        Phonetic = record.Phonetic,
        Difficulty = record.Difficulty,
        FrequencyRank = record.FrequencyRank,
        Definitions = record.Definitions
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Id)
            .Select(d => new WordDefinition
            {
                PartOfSpeech = d.PartOfSpeech,
                DefinitionZh = d.DefinitionZh,
                DefinitionEn = d.DefinitionEn,
                ExampleEn = d.ExampleEn,
                ExampleZh = d.ExampleZh,
            })
            .ToList(),
    };
}
