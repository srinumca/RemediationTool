using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.FileServices;

/// <summary>
/// Local file-system implementation used for development and file-share based execution.
/// A future SMB/NFS/S3 implementation can use the same application contract.
/// </summary>
public sealed class LocalQuarantineFileService : IQuarantineFileService
{
    private readonly QuarantineProcessingOptions _options;

    public LocalQuarantineFileService(IOptions<QuarantineProcessingOptions> options)
    {
        _options = options.Value;
        Directory.CreateDirectory(GetFullPath(_options.SourceRootPath));
        Directory.CreateDirectory(GetFullPath(_options.QuarantineRootPath));
    }

    public string ResolveSourcePath(FileFinding finding)
    {
        var sourcePath = !string.IsNullOrWhiteSpace(finding.CurrentFileLocation)
            ? finding.CurrentFileLocation
            : finding.OriginalFileLocation;

        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        return GetFullPath(sourcePath);
    }

    public string BuildQuarantinePath(FileFinding finding, string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? $"{finding.Id:N}.bin"
            : fileName;

        var sourceRecordPrefix = string.IsNullOrWhiteSpace(finding.SourceRecordId)
            ? finding.Id.ToString("N")
            : SanitizePathSegment(finding.SourceRecordId);

        return Path.Combine(
            GetFullPath(_options.QuarantineRootPath),
            DateTime.UtcNow.ToString("yyyy"),
            DateTime.UtcNow.ToString("MM"),
            sourceRecordPrefix,
            safeFileName);
    }

    public string BuildStubPath(string sourcePath)
        => sourcePath + _options.StubFileSuffix;

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(path) && File.Exists(path));

    public async Task CopyAsync(string sourcePath, string quarantinePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        if (string.IsNullOrWhiteSpace(quarantinePath))
            throw new ArgumentException("Quarantine path is required.", nameof(quarantinePath));

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourcePath}", sourcePath);

        var quarantineDirectory = Path.GetDirectoryName(quarantinePath);
        if (!string.IsNullOrWhiteSpace(quarantineDirectory))
            Directory.CreateDirectory(quarantineDirectory);

        await using var sourceStream = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destinationStream = File.Create(quarantinePath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    public async Task WriteStubAsync(string stubPath, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stubPath))
            throw new ArgumentException("Stub path is required.", nameof(stubPath));

        var directory = Path.GetDirectoryName(stubPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(stubPath, message, cancellationToken);
    }

    public Task DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        if (File.Exists(sourcePath))
            File.Delete(sourcePath);

        return Task.CompletedTask;
    }

    private static string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }
}
