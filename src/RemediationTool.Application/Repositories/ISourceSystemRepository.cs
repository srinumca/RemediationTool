using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Provides access to the canonical source-system configuration.
/// </summary>
public interface ISourceSystemRepository
{
    Task<SourceSystemDefinition?> GetBySourceSystemAsync(
        string sourceSystem,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceSystemDefinition>> GetEnabledAsync(
        CancellationToken cancellationToken = default);
}
