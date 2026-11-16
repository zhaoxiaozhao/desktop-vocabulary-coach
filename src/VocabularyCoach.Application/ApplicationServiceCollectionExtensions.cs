using Microsoft.Extensions.DependencyInjection;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Adaptive;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Application.Capture;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Quiz;
using VocabularyCoach.Application.Recommendation;
using VocabularyCoach.Application.Review;
using VocabularyCoach.Application.Scheduling;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Application.Updates;

namespace VocabularyCoach.Application;

/// <summary>Application 层 DI 注册入口。</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILicenseKeyVerifier, EcdsaLicenseKeyVerifier>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IAppVersion, AppVersion>();
        services.AddSingleton<PauseState>();
        services.AddSingleton<PopupRateLimiter>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IReviewScheduler, Sm2ReviewScheduler>();
        services.AddSingleton<IAdaptiveDifficultyService, AdaptiveDifficultyService>();
        services.AddSingleton<IVocabularyEnrichmentProvider, MockVocabularyEnrichmentProvider>();
        services.AddSingleton<IAiEnrichmentService, AiEnrichmentService>();
        services.AddSingleton<IWordQuizGenerator, WordQuizGenerator>();
        services.AddSingleton<WordCaptureService>();
        services.AddSingleton<IWordCaptureService>(sp => sp.GetRequiredService<WordCaptureService>());
        services.AddSingleton<WordRecommendationService>();
        services.AddSingleton<QuizWordCardSource>();
        services.AddSingleton<IWordCardSource>(sp => sp.GetRequiredService<QuizWordCardSource>());
        services.AddSingleton<WordCardService>();

        services.AddSingleton<PopupSchedulerService>();
        services.AddSingleton<ISchedulerStatus>(sp => sp.GetRequiredService<PopupSchedulerService>());
        services.AddHostedService(sp => sp.GetRequiredService<PopupSchedulerService>());

        return services;
    }
}
