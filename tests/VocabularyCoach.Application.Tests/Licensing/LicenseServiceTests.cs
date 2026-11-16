using Microsoft.Extensions.Logging.Abstractions;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Tests.Support;

namespace VocabularyCoach.Application.Tests.Licensing;

public class LicenseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initialize_StartsThirtyDayTrial()
    {
        var trialStore = new InMemoryTrialStore(new TrialState(Now, Now));
        var service = CreateService(trialStore: trialStore);

        await service.InitializeAsync();

        Assert.Equal(LicenseStatus.Trial, service.Current.Status);
        Assert.Equal(30, service.Current.DaysRemaining);
        Assert.True(service.CanUsePremiumFeatures);
    }

    [Fact]
    public async Task Initialize_ExpiresAfterThirtyDays()
    {
        var trialStore = new InMemoryTrialStore(new TrialState(Now.AddDays(-31), Now));
        var service = CreateService(trialStore: trialStore);

        await service.InitializeAsync();

        Assert.Equal(LicenseStatus.TrialExpired, service.Current.Status);
        Assert.False(service.CanUsePremiumFeatures);
    }

    [Fact]
    public async Task Initialize_TreatsClockRollbackAsExpired()
    {
        var trialStore = new InMemoryTrialStore(new TrialState(Now.AddDays(-2), Now.AddDays(2)));
        var service = CreateService(trialStore: trialStore);

        await service.InitializeAsync();

        Assert.Equal(LicenseStatus.TrialExpired, service.Current.Status);
        Assert.False(service.CanUsePremiumFeatures);
    }

    [Fact]
    public async Task Activate_WithValidKey_PersistsAndUnlocks()
    {
        var licenseStore = new InMemoryLicenseStore();
        var verifier = new StubVerifier
        {
            Success = true,
            Payload = new LicensePayload(1, "XHS-0001", "lifetime", Now, null),
        };
        var service = CreateService(licenseStore: licenseStore, verifier: verifier);
        await service.InitializeAsync();

        var result = await service.ActivateAsync("DVC1.test.test");

        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Licensed, service.Current.Status);
        Assert.Equal("XHS-0001", service.Current.LicenseId);
        Assert.Equal("DVC1.test.test", licenseStore.Value);
        Assert.True(service.CanUsePremiumFeatures);
    }

    [Fact]
    public async Task Activate_WithInvalidKey_DoesNotPersist()
    {
        var licenseStore = new InMemoryLicenseStore();
        var verifier = new StubVerifier { Success = false, Error = "bad key" };
        var service = CreateService(licenseStore: licenseStore, verifier: verifier);
        await service.InitializeAsync();

        var result = await service.ActivateAsync("bad");

        Assert.False(result.Success);
        Assert.Equal("bad key", result.Message);
        Assert.Null(licenseStore.Value);
        Assert.Equal(LicenseStatus.Trial, service.Current.Status);
    }

    [Fact]
    public async Task Initialize_LoadsSavedLicense()
    {
        var licenseStore = new InMemoryLicenseStore { Value = "DVC1.saved.sig" };
        var verifier = new StubVerifier
        {
            Success = true,
            Payload = new LicensePayload(1, "OWNER-1", "lifetime", Now, null),
        };
        var service = CreateService(licenseStore: licenseStore, verifier: verifier);

        await service.InitializeAsync();

        Assert.Equal(LicenseStatus.Licensed, service.Current.Status);
        Assert.Equal("OWNER-1", service.Current.LicenseId);
        Assert.True(service.CanUsePremiumFeatures);
    }

    private static LicenseService CreateService(
        InMemoryLicenseStore? licenseStore = null,
        InMemoryTrialStore? trialStore = null,
        StubVerifier? verifier = null) =>
        new(
            licenseStore ?? new InMemoryLicenseStore(),
            trialStore ?? new InMemoryTrialStore(null),
            verifier ?? new StubVerifier(),
            new FakeTimeProvider(Now),
            NullLogger<LicenseService>.Instance);

    private sealed class InMemoryLicenseStore : ILicenseStore
    {
        public string? Value { get; set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Value);

        public Task WriteAsync(string licenseKey, CancellationToken cancellationToken = default)
        {
            Value = licenseKey;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryTrialStore(TrialState? state) : ITrialStateStore
    {
        private TrialState? _state = state;

        public Task<TrialState?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        public Task SaveAsync(TrialState value, CancellationToken cancellationToken = default)
        {
            _state = value;
            return Task.CompletedTask;
        }
    }

    private sealed class StubVerifier : ILicenseKeyVerifier
    {
        public bool Success { get; set; }

        public LicensePayload? Payload { get; set; }

        public string Error { get; set; } = "invalid";

        public bool TryVerify(string licenseKey, out LicensePayload? payload, out string error)
        {
            payload = Payload;
            error = Success ? string.Empty : Error;
            return Success;
        }
    }
}
