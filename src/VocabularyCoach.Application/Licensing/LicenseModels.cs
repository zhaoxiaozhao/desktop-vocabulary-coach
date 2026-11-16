using System.Text.Json.Serialization;

namespace VocabularyCoach.Application.Licensing;

/// <summary>许可证状态。</summary>
public enum LicenseStatus
{
    Trial,
    TrialExpired,
    Licensed,
}

/// <summary>当前许可证快照；由 <see cref="ILicenseService"/> 在读取时按当前时间计算。</summary>
public sealed record LicenseSnapshot(
    LicenseStatus Status,
    bool CanUsePremiumFeatures,
    DateTimeOffset? TrialEndsAt,
    int DaysRemaining,
    string LicenseId,
    string Edition);

/// <summary>激活结果。</summary>
public sealed record LicenseActivationResult(bool Success, string Message);

/// <summary>激活码中携带的载荷。</summary>
public sealed record LicensePayload(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("edition")] string Edition,
    [property: JsonPropertyName("iat")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("note")] string? Note);

/// <summary>试用开始时间与最后见到的时间（用于检测时钟回拨）。</summary>
public sealed record TrialState(DateTimeOffset FirstStartedAt, DateTimeOffset LastSeenAt);
