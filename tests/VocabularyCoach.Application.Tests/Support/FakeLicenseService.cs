using VocabularyCoach.Application.Licensing;

namespace VocabularyCoach.Application.Tests.Support;

internal sealed class FakeLicenseService : ILicenseService
{
    public LicenseSnapshot Current { get; set; } = new(
        LicenseStatus.Trial,
        CanUsePremiumFeatures: true,
        TrialEndsAt: DateTimeOffset.UtcNow.AddDays(30),
        DaysRemaining: 30,
        LicenseId: string.Empty,
        Edition: string.Empty);

    public bool CanUsePremiumFeatures => Current.CanUsePremiumFeatures;

    public event EventHandler? Changed;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<LicenseActivationResult> ActivateAsync(
        string licenseKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new LicenseActivationResult(true, "ok"));

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Expire()
    {
        Current = Current with
        {
            Status = LicenseStatus.TrialExpired,
            CanUsePremiumFeatures = false,
            DaysRemaining = 0,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
