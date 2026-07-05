using System.Text;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.FileServices;

/// <summary>
/// Storage-backed quarantine implementation used when Storage:Type is S3 or any
/// non-local IStorageService implementation is configured.
/// Keeps quarantine compatible with the same storage abstraction used by ingestion.
/// </summary>
public sealed class StorageQuarantineFileService : IQuarantineFileService
{
    private readonly IStorageService _storageService;
    private readonly QuarantineProcessingOptions _options;

    public StorageQuarantineFileService(
        IStorageService storageService,
        IOptions<QuarantineProcessingOptions> options)
    {
        _storageService = storageService;
        _options = options.Value;
    }

    public string ResolveSourcePath(FileFinding finding)
    {
        var sourcePath = !string.IsNullOrWhiteSpace(finding.CurrentFileLocation)
            ? finding.CurrentFileLocation
            : finding.OriginalFileLocation;

        return NormalizeStorageKey(sourcePath ?? string.Empty);
    }

    public string BuildQuarantinePath(FileFinding finding, string sourcePath)
    {
        var fileName = GetFileNameFromKey(sourcePath);
        var safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? $"{finding.Id:N}.bin"
            : fileName;

        var sourceRecordPrefix = string.IsNullOrWhiteSpace(finding.SourceRecordId)
            ? finding.Id.ToString("N")
            : SanitizeStorageSegment(finding.SourceRecordId);

        return CombineStorageKey(
            _options.QuarantineRootPath,
            DateTime.UtcNow.ToString("yyyy"),
            DateTime.UtcNow.ToString("MM"),
            sourceRecordPrefix,
            safeFileName);
    }

    public string BuildStubPath(string sourcePath)
        => NormalizeStorageKey(sourcePath) + _options.StubFileSuffix;

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            await using var stream = await _storageService.DownloadAsync(NormalizeStorageKey(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task CopyAsync(string sourcePath, string quarantinePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        if (string.IsNullOrWhiteSpace(quarantinePath))
            throw new ArgumentException("Quarantine path is required.", nameof(quarantinePath));

        await using var sourceStream = await _storageService.DownloadAsync(NormalizeStorageKey(sourcePath));
        await using var copyStream = new MemoryStream();
        await sourceStream.CopyToAsync(copyStream, cancellationToken);
        copyStream.Position = 0;

        await _storageService.UploadAsync(NormalizeStorageKey(quarantinePath), copyStream);
    }

    public async Task WriteStubAsync(string stubPath, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stubPath))
            throw new ArgumentException("Stub path is required.", nameof(stubPath));

        var content = Encoding.UTF8.GetBytes(message ?? string.Empty);
        await using var stream = new MemoryStream(content);
        await _storageService.UploadAsync(NormalizeStorageKey(stubPath), stream);
    }

    public Task DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        return _storageService.DeleteAsync(NormalizeStorageKey(sourcePath));
    }

    private static string CombineStorageKey(params string?[] segments)
        => string.Join(
            "/",
            segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => NormalizeStorageKey(segment!)));

    private static string NormalizeStorageKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var normalized = key.Trim().Replace('\\', '/');

        if (normalized.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
        {
            var withoutScheme = normalized[5..];
            var firstSlashIndex = withoutScheme.IndexOf('/');
            normalized = firstSlashIndex >= 0 ? withoutScheme[(firstSlashIndex + 1)..] : string.Empty;
        }

        return normalized.Trim('/');
    }

    private static string GetFileNameFromKey(string key)
    {
        var normalized = NormalizeStorageKey(key);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var lastSlashIndex = normalized.LastIndexOf('/');
        return lastSlashIndex >= 0 ? normalized[(lastSlashIndex + 1)..] : normalized;
    }

    private static string SanitizeStorageSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }
}
