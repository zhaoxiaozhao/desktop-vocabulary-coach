namespace VocabularyCoach.Infrastructure.Persistence.Entities;

/// <summary>AppSettings 表：键值型设置，Sprint 5 的设置界面会写入这里。</summary>
public sealed class AppSettingRecord
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
