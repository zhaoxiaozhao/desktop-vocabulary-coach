using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Persistence.Entities;

namespace VocabularyCoach.Infrastructure.Dictionary;

/// <summary>数据库初始化结果，供启动日志与主窗口展示。</summary>
public sealed record DatabaseInitializationResult(int WordCount, WordImportResult? SeedImport);

/// <summary>
/// 启动时的数据库引导：执行 EF 迁移、确保用户档案存在、按版本导入内置种子词库。
/// 导入是幂等的，升级词库不会覆盖用户学习进度。
/// </summary>
public sealed class DatabaseInitializer
{
    private const string SeedSettingKey = "seed.version";
    private const string SeedRelativePath = "Seed/words.seed.json";

    private readonly IDbContextFactory<VocabularyCoachDbContext> _dbContextFactory;
    private readonly IWordLibraryImporter _importer;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<VocabularyCoachDbContext> dbContextFactory,
        IWordLibraryImporter importer,
        ILogger<DatabaseInitializer> logger)
    {
        _dbContextFactory = dbContextFactory;
        _importer = importer;
        _logger = logger;
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("数据库迁移检查完成。");

        if (!await db.UserLearningProfiles.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            db.UserLearningProfiles.Add(new UserLearningProfileRecord
            {
                CurrentDifficulty = 2,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        WordImportResult? seedImport = null;
        var seedPath = Path.Combine(AppContext.BaseDirectory, SeedRelativePath);

        var seedVersion = await TryGetSeedVersionAsync(seedPath, cancellationToken).ConfigureAwait(false);
        var installedVersion = await db.AppSettings
            .AsNoTracking()
            .Where(x => x.Key == SeedSettingKey)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (seedVersion > 0 && installedVersion != seedVersion.ToString())
        {
            seedImport = await ImportSeedAsync(seedPath, cancellationToken).ConfigureAwait(false);

            if (seedImport is not null)
            {
                var setting = await db.AppSettings
                    .FirstOrDefaultAsync(x => x.Key == SeedSettingKey, cancellationToken)
                    .ConfigureAwait(false);

                if (setting is null)
                {
                    db.AppSettings.Add(new AppSettingRecord
                    {
                        Key = SeedSettingKey,
                        Value = seedVersion.ToString(),
                        UpdatedAt = DateTimeOffset.UtcNow,
                    });
                }
                else
                {
                    setting.Value = seedVersion.ToString();
                    setting.UpdatedAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var wordCount = await db.Words.CountAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("数据库初始化完成，词库共 {WordCount} 个单词。", wordCount);

        return new DatabaseInitializationResult(wordCount, seedImport);
    }

    private async Task<WordImportResult?> ImportSeedAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("未找到内置词库文件：{Path}", path);
            return null;
        }

        await using var stream = File.OpenRead(path);
        var result = await _importer.ImportAsync(stream, WordImportFormat.Json, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("内置词库导入完成：{Summary}", result);
        return result;
    }

    private static async Task<int> TryGetSeedVersionAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return document.RootElement.TryGetProperty("version", out var version) &&
               version.TryGetInt32(out var value)
            ? value
            : 1;
    }
}
