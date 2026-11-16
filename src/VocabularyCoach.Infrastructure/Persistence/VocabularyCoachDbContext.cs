using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Persistence;

/// <summary>
/// 本地 SQLite 数据上下文。库文件位于 %LocalAppData%\DesktopVocabularyCoach\vocabulary.db，
/// 绝不放在安装目录。
/// </summary>
public sealed class VocabularyCoachDbContext : DbContext
{
    public VocabularyCoachDbContext(DbContextOptions<VocabularyCoachDbContext> options)
        : base(options)
    {
    }

    public DbSet<WordRecord> Words => Set<WordRecord>();

    public DbSet<WordDefinitionRecord> WordDefinitions => Set<WordDefinitionRecord>();

    public DbSet<UserWordProgressRecord> UserWordProgress => Set<UserWordProgressRecord>();

    public DbSet<ReviewLogRecord> ReviewLogs => Set<ReviewLogRecord>();

    public DbSet<LearningSessionRecord> LearningSessions => Set<LearningSessionRecord>();

    public DbSet<UserLearningProfileRecord> UserLearningProfiles => Set<UserLearningProfileRecord>();

    public DbSet<AppSettingRecord> AppSettings => Set<AppSettingRecord>();

    public DbSet<AiGenerationCacheRecord> AiGenerationCache => Set<AiGenerationCacheRecord>();

    public DbSet<WordCaptureRecord> WordCaptures => Set<WordCaptureRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 所有时间统一以 UTC 存储；SQLite 以 ISO-8601 "O" 字符串保存，可直接按字典序比较。
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WordRecord>(entity =>
        {
            entity.ToTable("Words");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Word).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedWord).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Phonetic).HasMaxLength(128);
            entity.Property(x => x.Source).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.NormalizedWord).IsUnique();
            entity.HasIndex(x => x.Difficulty);
            entity.HasIndex(x => x.FrequencyRank);
            entity.HasMany(x => x.Definitions)
                .WithOne(x => x.Word)
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WordDefinitionRecord>(entity =>
        {
            entity.ToTable("WordDefinitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DefinitionZh).IsRequired();
            entity.HasIndex(x => new { x.WordId, x.SortOrder });
        });

        modelBuilder.Entity<UserWordProgressRecord>(entity =>
        {
            entity.ToTable("UserWordProgress");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => x.WordId).IsUnique();
            entity.HasIndex(x => x.DueAt);
            entity.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewLogRecord>(entity =>
        {
            entity.ToTable("ReviewLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Rating).HasConversion<int>();
            entity.Property(x => x.UserFeedback).HasConversion<int>();
            entity.Property(x => x.Source).HasConversion<int>();
            entity.HasIndex(x => x.WordId);
            entity.HasIndex(x => x.ReviewedAt);
            entity.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LearningSessionRecord>(entity =>
        {
            entity.ToTable("LearningSessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasConversion<int>();
            entity.Property(x => x.Source).HasConversion<int>();
            entity.HasIndex(x => x.WordId);
            entity.HasIndex(x => x.ShownAt);
            entity.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserLearningProfileRecord>(entity =>
        {
            entity.ToTable("UserLearningProfile");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<AppSettingRecord>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(128);
            entity.Property(x => x.Value).IsRequired();
        });

        modelBuilder.Entity<AiGenerationCacheRecord>(entity =>
        {
            entity.ToTable("AiGenerationCache");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Model).HasMaxLength(128);
            entity.Property(x => x.Content).IsRequired();
            entity.HasIndex(x => new { x.WordId, x.Provider }).IsUnique();
            entity.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WordCaptureRecord>(entity =>
        {
            entity.ToTable("WordCaptures");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RawText).HasMaxLength(128).IsRequired();
            entity.Property(x => x.NormalizedWord).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.NormalizedWord).IsUnique();
            entity.HasIndex(x => x.CapturedAt);
            entity.HasOne(x => x.Word)
                .WithMany()
                .HasForeignKey(x => x.WordId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>把 DateTimeOffset 规范化为 UTC 的 ISO-8601 字符串存储。</summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
    {
    }
}
