using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

/// <summary>
/// Abstraction over the physical data-system operations required by quarantine.
/// Application services should not call System.IO directly.
/// </summary>
public interface IQuarantineFileService
{
    string ResolveSourcePath(FileFinding finding);

    string BuildQuarantinePath(FileFinding finding, string sourcePath);

    string BuildStubPath(string sourcePath);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task CopyAsync(string sourcePath, string quarantinePath, CancellationToken cancellationToken = default);

    Task WriteStubAsync(string stubPath, string message, CancellationToken cancellationToken = default);

    Task DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default);
}
