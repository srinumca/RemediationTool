using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.FileServices;

/// <summary>
/// Storage-backed quarantine implementation used when Storage:Type is S3 or any
/// non-local IStorageService implementation is configured.
/// </summary>
public sealed class StorageQuarantineFileService : IQuarantineFileService
{
    private static readonly HashSet<char> InvalidStorageSegmentChars =
        new(Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }));

    private readonly IStorageService _storageService;
    private readonly QuarantineProcessingOptions _options;
    private readonly ILogger<StorageQuarantineFileService> _logger;

    public StorageQuarantineFileService(
        IStorageService storageService,
        IOptions<QuarantineProcessingOptions> options,
        ILogger<StorageQuarantineFileService> logger)
    {
        _storageService = storageService;
        _options = options.Value;
        _logger = logger;

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_INIT] QuarantineRootPath:{QuarantineRootPath}, SourceRootPath:{SourceRootPath}, StubFileSuffix:{StubFileSuffix}",
            _options.QuarantineRootPath,
            _options.SourceRootPath,
            _options.StubFileSuffix);
    }

    public string ResolveSourcePath(FileFinding finding)
    {
        var sourcePath = !string.IsNullOrWhiteSpace(finding.CurrentFileLocation)
            ? finding.CurrentFileLocation
            : finding.OriginalFileLocation;

        var normalizedPath = NormalizeStorageKey(sourcePath ?? string.Empty);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_SOURCE_RESOLVED] RecordId:{RecordId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, RawSourcePath:{RawSourcePath}, NormalizedSourcePath:{NormalizedSourcePath}",
            finding.Id,
            finding.SourceRecordId,
            finding.FileName,
            sourcePath,
            normalizedPath);

        return normalizedPath;
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

        var nowUtc = DateTime.UtcNow;
        var quarantinePath = CombineStorageKey(
            _options.QuarantineRootPath,
            nowUtc.ToString("yyyy"),
            nowUtc.ToString("MM"),
            sourceRecordPrefix,
            safeFileName);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_PATH_BUILT] RecordId:{RecordId}, SourceRecordId:{SourceRecordId}, SourcePath:{SourcePath}, QuarantinePath:{QuarantinePath}",
            finding.Id,
            finding.SourceRecordId,
            sourcePath,
            quarantinePath);

        return quarantinePath;
    }

    public string BuildStubPath(string sourcePath)
    {
        var stubPath = NormalizeStorageKey(sourcePath) + _options.StubFileSuffix;

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_STUB_PATH_BUILT] SourcePath:{SourcePath}, StubPath:{StubPath}",
            sourcePath,
            stubPath);

        return stubPath;
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("[STORAGE_QUARANTINE_EXISTS_SKIPPED] Path is empty.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeStorageKey(path);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_EXISTS_START] Path:{Path}",
            normalizedPath);

        try
        {
            var exists = await _storageService.ExistsAsync(normalizedPath);

            _logger.LogInformation(
                "[STORAGE_QUARANTINE_EXISTS_COMPLETE] Path:{Path}, Exists:{Exists}",
                normalizedPath,
                exists);

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[STORAGE_QUARANTINE_EXISTS_FAILED] Path:{Path}, Exists:false, Error:{Error}",
                normalizedPath,
                ex.Message);

            return false;
        }
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

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSourcePath = NormalizeStorageKey(sourcePath);
        var normalizedQuarantinePath = NormalizeStorageKey(quarantinePath);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_COPY_START] SourcePath:{SourcePath}, DestinationPath:{DestinationPath}",
            normalizedSourcePath,
            normalizedQuarantinePath);

        await using var sourceStream = await _storageService.DownloadAsync(normalizedSourcePath);
        if (sourceStream.CanSeek)
            sourceStream.Position = 0;

        var sizeBytes = sourceStream.CanSeek ? sourceStream.Length : (long?)null;
        await _storageService.UploadAsync(normalizedQuarantinePath, sourceStream);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_COPY_COMPLETE] SourcePath:{SourcePath}, DestinationPath:{DestinationPath}, SizeBytes:{SizeBytes}",
            normalizedSourcePath,
            normalizedQuarantinePath,
            sizeBytes);
    }

    public async Task WriteStubAsync(
        string stubPath,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stubPath))
            throw new ArgumentException("Stub path is required.", nameof(stubPath));

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedStubPath = NormalizeStorageKey(stubPath);
        var content = Encoding.UTF8.GetBytes(message ?? string.Empty);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_STUB_WRITE_START] StubPath:{StubPath}, SizeBytes:{SizeBytes}",
            normalizedStubPath,
            content.Length);

        await using var stream = new MemoryStream(content, writable: false);
        await _storageService.UploadAsync(normalizedStubPath, stream);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_STUB_WRITE_COMPLETE] StubPath:{StubPath}, SizeBytes:{SizeBytes}",
            normalizedStubPath,
            content.Length);
    }

    public async Task DeleteSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedSourcePath = NormalizeStorageKey(sourcePath);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_DELETE_START] Path:{Path}",
            normalizedSourcePath);

        await _storageService.DeleteAsync(normalizedSourcePath);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_DELETE_COMPLETE] Path:{Path}",
            normalizedSourcePath);
    }

    private static string CombineStorageKey(params string?[] segments)
    {
        var normalizedSegments = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (!string.IsNullOrWhiteSpace(segment))
                normalizedSegments.Add(NormalizeStorageKey(segment));
        }

        return string.Join('/', normalizedSegments);
    }

    private static string NormalizeStorageKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var normalized = key.Trim().Replace('\\', '/');

        if (normalized.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
        {
            var withoutScheme = normalized[5..];
            var firstSlashIndex = withoutScheme.IndexOf('/');
            normalized = firstSlashIndex >= 0
                ? withoutScheme[(firstSlashIndex + 1)..]
                : string.Empty;
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
        char[]? sanitized = null;

        for (var index = 0; index < value.Length; index++)
        {
            if (!InvalidStorageSegmentChars.Contains(value[index]))
                continue;

            sanitized ??= value.ToCharArray();
            sanitized[index] = '_';
        }

        var result = sanitized == null ? value : new string(sanitized);
        return string.IsNullOrWhiteSpace(result) ? Guid.NewGuid().ToString("N") : result;
    }
}
