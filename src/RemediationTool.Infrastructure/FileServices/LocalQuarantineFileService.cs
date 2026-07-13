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
    private const int FileBufferSize = 81920;
    private static readonly HashSet<char> InvalidPathSegmentChars =
        new(Path.GetInvalidFileNameChars());

    private readonly QuarantineProcessingOptions _options;
    private readonly string _sourceRootPath;
    private readonly string _quarantineRootPath;

    public LocalQuarantineFileService(IOptions<QuarantineProcessingOptions> options)
    {
        _options = options.Value;
        _sourceRootPath = GetFullPath(_options.SourceRootPath);
        _quarantineRootPath = GetFullPath(_options.QuarantineRootPath);

        Directory.CreateDirectory(_sourceRootPath);
        Directory.CreateDirectory(_quarantineRootPath);
    }

    public string ResolveSourcePath(FileFinding finding)
    {
        var sourcePath = !string.IsNullOrWhiteSpace(finding.CurrentFileLocation)
            ? finding.CurrentFileLocation
            : finding.OriginalFileLocation;

        return string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : GetFullPath(sourcePath);
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

        var nowUtc = DateTime.UtcNow;
        return Path.Combine(
            _quarantineRootPath,
            nowUtc.ToString("yyyy"),
            nowUtc.ToString("MM"),
            sourceRecordPrefix,
            safeFileName);
    }

    public string BuildStubPath(string sourcePath)
        => sourcePath + _options.StubFileSuffix;

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public async Task CopyAsync(
        string sourcePath,
        string quarantinePath,
        CancellationToken cancellationToken = default)
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

        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destinationStream = new FileStream(
            quarantinePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    public async Task WriteStubAsync(
        string stubPath,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stubPath))
            throw new ArgumentException("Stub path is required.", nameof(stubPath));

        var directory = Path.GetDirectoryName(stubPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(stubPath, message, cancellationToken);
    }

    public Task DeleteSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        cancellationToken.ThrowIfCancellationRequested();

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
        char[]? sanitized = null;

        for (var index = 0; index < value.Length; index++)
        {
            if (!InvalidPathSegmentChars.Contains(value[index]))
                continue;

            sanitized ??= value.ToCharArray();
            sanitized[index] = '_';
        }

        var result = sanitized == null ? value : new string(sanitized);
        return string.IsNullOrWhiteSpace(result) ? Guid.NewGuid().ToString("N") : result;
    }
}
