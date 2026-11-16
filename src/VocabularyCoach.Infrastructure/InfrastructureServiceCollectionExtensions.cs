using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Backup;
using VocabularyCoach.Application.Capture;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Statistics;
using VocabularyCoach.Application.Updates;
using VocabularyCoach.Infrastructure.Ai;
using VocabularyCoach.Infrastructure.Audio;
using VocabularyCoach.Infrastructure.Dictionary;
using VocabularyCoach.Infrastructure.Diagnostics;
using VocabularyCoach.Infrastructure.Licensing;
using VocabularyCoach.Infrastructure.Persistence;
using VocabularyCoach.Infrastructure.Repositories;
using VocabularyCoach.Infrastructure.Statistics;
using VocabularyCoach.Infrastructure.SystemIntegration;
using VocabularyCoach.Infrastructure.Updates;

namespace VocabularyCoach.Infrastructure;

/// <summary>Infrastructure 层 DI 注册入口。</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var databaseDirectory = Path.GetDirectoryName(AppPaths.DatabaseFile)!;
        AppPaths.EnsureDirectory(databaseDirectory);

        services.AddDbContextFactory<VocabularyCoachDbContext>((sp, options) =>
        {
            options.UseSqlite($"Data Source={AppPaths.DatabaseFile}");
            options.UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>());
            options.EnableDetailedErrors();
        });

        services.AddSingleton<IWordLibraryImporter, WordLibraryImporter>();
        services.AddSingleton<IWordLibrary, EfWordLibrary>();
        services.AddSingleton<ILearningStore, EfLearningStore>();
        services.AddSingleton<IWordCaptureStore, EfWordCaptureStore>();
        services.AddSingleton<ISettingsStore, EfSettingsStore>();
        services.AddSingleton<IStatisticsService, EfStatisticsService>();
        services.AddSingleton(new DatabaseLocation(AppPaths.DatabaseFile));
        services.AddSingleton<IDatabaseBackupService, SqliteDatabaseBackupService>();

        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<IVocabularyEnrichmentProvider, OpenAiCompatibleEnrichmentProvider>();
        services.AddSingleton<IAiCacheStore, EfAiCacheStore>();
        services.AddSingleton<ISecretStore>(sp => new DpapiSecretStore(
            Path.Combine(AppPaths.RootDirectory, "ai.key"),
            sp.GetRequiredService<ILogger<DpapiSecretStore>>()));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IAudioPronouncer, SystemSpeechPronouncer>();
        services.AddSingleton<ISystemStateMonitor, WindowsSystemStateMonitor>();
        services.AddSingleton<ISystemStartupManager>(_ => new WindowsStartupManager());
        services.AddSingleton<ILicenseStore>(sp => new DpapiLicenseStore(
            Path.Combine(AppPaths.RootDirectory, "license.dat"),
            sp.GetRequiredService<ILogger<DpapiLicenseStore>>()));
        services.AddSingleton<ITrialStateStore>(_ => new WindowsTrialStateStore(
            Path.Combine(AppPaths.RootDirectory, "trial.json")));
        services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));
        services.AddSingleton<IUpdateService, HttpUpdateService>();

        return services;
    }
}
