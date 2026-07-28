using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Local/JSON persistence fallback so development mode remains usable without DynamoDB.
/// Production DynamoDB deployments use <see cref="DynamoDbSourceSystemRepository"/>.
/// </summary>
public sealed class LocalSourceSystemRepository : ISourceSystemRepository
{
    private static readonly SourceSystemDefinition NetApp = new()
    {
        SourceSystem = "NetApp",
        DataSystem = "NetApp",
        Description = "On-premises NetApp filer integration",
        DisplayName = "NetApp File Server",
        IsEnabled = true,
        OriginatingDataSystem = "smb"
    };

    public Task<SourceSystemDefinition?> GetBySourceSystemAsync(
        string sourceSystem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SourceSystemDefinition? result = string.Equals(
            sourceSystem?.Trim(),
            NetApp.SourceSystem,
            StringComparison.Ordinal)
            ? NetApp
            : null;

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<SourceSystemDefinition>> GetEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SourceSystemDefinition> results = new[] { NetApp };
        return Task.FromResult(results);
    }
}
