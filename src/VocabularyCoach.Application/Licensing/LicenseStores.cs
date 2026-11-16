namespace VocabularyCoach.Application.Licensing;

/// <summary>许可证的持久化（实现方负责加密）。</summary>
public interface ILicenseStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(string licenseKey, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>试用状态的持久化（文件 + 注册表等多处冗余）。</summary>
public interface ITrialStateStore
{
    Task<TrialState?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TrialState state, CancellationToken cancellationToken = default);
}
