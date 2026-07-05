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
/// Keeps quarantine compatible with the same storage abstraction used by ingestion.
/// </summary>
public sealed class StorageQuarantineFileService : IQuarantineFileService
{
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

        var quarantinePath = CombineStorageKey(
            _options.QuarantineRootPath,
            DateTime.UtcNow.ToString("yyyy"),
            DateTime.UtcNow.ToString("MM"),
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

        var normalizedPath = NormalizeStorageKey(path);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_EXISTS_START] Path:{Path}",
            normalizedPath);

        try
        {
            await using var stream = await _storageService.DownloadAsync(normalizedPath);

            _logger.LogInformation(
                "[STORAGE_QUARANTINE_EXISTS_COMPLETE] Path:{Path}, Exists:true, SizeBytes:{SizeBytes}",
                normalizedPath,
                stream.CanSeek ? stream.Length : null);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[STORAGE_QUARANTINE_EXISTS_FAILED] Path:{Path}, Exists:false, Error:{Error}",
                normalizedPath,
                ex.Message);

            return false;
        }
    }

    public async Task CopyAsync(string sourcePath, string quarantinePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        if (string.IsNullOrWhiteSpace(quarantinePath))
            throw new ArgumentException("Quarantine path is required.", nameof(quarantinePath));

        var normalizedSourcePath = NormalizeStorageKey(sourcePath);
        var normalizedQuarantinePath = NormalizeStorageKey(quarantinePath);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_COPY_START] SourcePath:{SourcePath}, DestinationPath:{DestinationPath}",
            normalizedSourcePath,
            normalizedQuarantinePath);

        await using var sourceStream = await _storageService.DownloadAsync(normalizedSourcePath);
        await using var copyStream = new MemoryStream();
        await sourceStream.CopyToAsync(copyStream, cancellationToken);
        copyStream.Position = 0;

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_COPY_DOWNLOADED] SourcePath:{SourcePath}, SizeBytes:{SizeBytes}",
            normalizedSourcePath,
            copyStream.Length);

        await _storageService.UploadAsync(normalizedQuarantinePath, copyStream);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_COPY_COMPLETE] SourcePath:{SourcePath}, DestinationPath:{DestinationPath}, SizeBytes:{SizeBytes}",
            normalizedSourcePath,
            normalizedQuarantinePath,
            copyStream.Length);
    }

    public async Task WriteStubAsync(string stubPath, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stubPath))
            throw new ArgumentException("Stub path is required.", nameof(stubPath));

        var normalizedStubPath = NormalizeStorageKey(stubPath);
        var content = Encoding.UTF8.GetBytes(message ?? string.Empty);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_STUB_WRITE_START] StubPath:{StubPath}, SizeBytes:{SizeBytes}",
            normalizedStubPath,
            content.Length);

        await using var stream = new MemoryStream(content);
        await _storageService.UploadAsync(normalizedStubPath, stream);

        _logger.LogInformation(
            "[STORAGE_QUARANTINE_STUB_WRITE_COMPLETE] StubPath:{StubPath}, SizeBytes:{SizeBytes}",
            normalizedStubPath,
            content.Length);
    }

    public async Task DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

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
