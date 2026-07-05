using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Restore service — restores quarantined files to their original location.
/// Uses the configured quarantine file service so restore works for both local and S3 storage.
/// </summary>
public class RestoreService
{
    private readonly IFileFindingRepository _repository;
    private readonly IQuarantineFileService _fileService;
    private readonly ILogger<RestoreService> _logger;
    private readonly IAuditLogger _auditLogger;

    public RestoreService(
        IFileFindingRepository repository,
        IQuarantineFileService fileService,
        ILogger<RestoreService> logger,
        IAuditLogger auditLogger)
    {
        _repository = repository;
        _fileService = fileService;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task RestoreAsync(Guid id)
    {
        _logger.LogInformation("[RESTORE START] FileId: {Id}", id);

        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogError("[RESTORE NOT FOUND] FileId: {Id} — no matching record.", id);
            return;
        }

        if (file.Status == FileStatus.RestorationComplete)
        {
            file.ErrorCategory = ErrorCategoryResolver.DuplicateRestoreAttempt().ToString();
            file.ErrorReason = "Restore skipped because the file is already restored.";
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogWarning(
                "[RESTORE DUPLICATE] FileId: {Id} FileName: {File} — file is already restored.",
                id, file.FileName);
            return;
        }

        if (file.Status != FileStatus.QuarantineComplete &&
            file.Status != FileStatus.PendingRestore)
        {
            _logger.LogWarning(
                "[RESTORE SKIPPED] FileId: {Id} FileName: {File} — file is not in a restorable state (current: {Status}).",
                id, file.FileName, file.Status);
            return;
        }

        // Capture paths before changing Status to InProgress.
        // FileFinding.QuarantinePath returns a value only when Status == QuarantineComplete.
        var quarantinePath = ResolveQuarantinePath(file);
        var originalPath = file.OriginalFileLocation;
        var stubPath = string.IsNullOrWhiteSpace(originalPath)
            ? string.Empty
            : _fileService.BuildStubPath(originalPath);

        try
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                MarkFailed(
                    file,
                    "Restore target path is missing.",
                    ErrorCategoryResolver.TargetPathMissing(),
                    id,
                    "Restore target path is missing",
                    new { file.FileName });
                return;
            }

            if (string.IsNullOrWhiteSpace(quarantinePath) ||
                !await _fileService.ExistsAsync(quarantinePath))
            {
                MarkFailed(
                    file,
                    $"Quarantine file not found: {quarantinePath}",
                    ErrorCategoryResolver.QuarantineFileMissing(),
                    id,
                    "Quarantine file not found",
                    new { file.FileName, quarantinePath });
                return;
            }

            file.Status = FileStatus.InProgress;
            file.UpdatedDate = DateTime.UtcNow;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            _repository.Update(file);

            await _fileService.CopyAsync(quarantinePath, originalPath);
            await _fileService.DeleteSourceAsync(quarantinePath);

            _logger.LogInformation(
                "[RESTORE FILE] FileId: {Id} FileName: {File} — copied from {QuarantinePath} to {OriginalPath}.",
                id, file.FileName, quarantinePath, originalPath);

            if (!string.IsNullOrWhiteSpace(stubPath) && await _fileService.ExistsAsync(stubPath))
            {
                await _fileService.DeleteSourceAsync(stubPath);
                _logger.LogInformation(
                    "[RESTORE STUB] FileId: {Id} FileName: {File} — retention placeholder removed at {StubPath}.",
                    id, file.FileName, stubPath);
            }

            file.Status = FileStatus.RestorationComplete;
            file.RestoredDateUtc = DateTime.UtcNow;
            file.CurrentFileLocation = originalPath;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation(
                "[RESTORE COMPLETE] FileId: {Id} FileName: {File} — restored to {OriginalPath}.",
                id, file.FileName, originalPath);

            _auditLogger.RecordEvent(
                eventType: "FileRestored",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Success",
                details: new
                {
                    file.FileName,
                    RestoredToPath = originalPath,
                    QuarantinePath = quarantinePath,
                    StubPath = stubPath
                });
        }
        catch (Exception ex)
        {
            var category = ErrorCategoryResolver.FromException(ex);

            file.Status = FileStatus.Error;
            file.ErrorReason = ex.Message;
            file.ErrorCategory = category.ToString();
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogError(ex,
                "[RESTORE FAILED] FileId: {Id} FileName: {File} — restore failed. Error: {Error}",
                id, file.FileName, ex.Message);

            _auditLogger.RecordEvent(
                eventType: "FileRestored",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Failed",
                details: new
                {
                    file.FileName,
                    Error = ex.Message,
                    ErrorCategory = category.ToString(),
                    QuarantinePath = quarantinePath,
                    OriginalPath = originalPath
                });
        }
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete ||
                        x.Status == FileStatus.PendingRestore)
            .ToList();

        _logger.LogInformation("[RESTORE ALL START] Found {Count} file(s) eligible for restore.", files.Count);

        var succeeded = 0;
        var failed = 0;

        foreach (var file in files)
        {
            await RestoreAsync(file.Id);

            var updated = _repository.GetById(file.Id);
            if (updated?.Status == FileStatus.RestorationComplete)
                succeeded++;
            else if (updated?.Status == FileStatus.Error)
                failed++;
        }

        if (succeeded > 0 && failed > 0)
        {
            _logger.LogWarning(
                "[RESTORE ALL PARTIAL FAILURE] Succeeded: {Succeeded}, Failed: {Failed}, ErrorCategory: {ErrorCategory}",
                succeeded, failed, ErrorCategoryResolver.PartialRestoreFailure());
        }

        _logger.LogInformation("[RESTORE ALL COMPLETE] Processed {Count} file(s). Succeeded: {Succeeded}, Failed: {Failed}",
            files.Count, succeeded, failed);
    }

    private string? ResolveQuarantinePath(FileFinding file)
    {
        if (file.Status == FileStatus.QuarantineComplete)
            return file.QuarantinePath;

        return string.IsNullOrWhiteSpace(file.CurrentFileLocation)
            ? file.QuarantinePath
            : file.CurrentFileLocation;
    }

    private void MarkFailed(
        FileFinding file,
        string errorReason,
        ErrorCategory errorCategory,
        Guid id,
        string auditError,
        object auditDetails)
    {
        file.Status = FileStatus.Error;
        file.ErrorReason = errorReason;
        file.ErrorCategory = errorCategory.ToString();
        file.UpdatedDate = DateTime.UtcNow;
        _repository.Update(file);

        _logger.LogError(
            "[RESTORE FAILED] FileId: {Id} FileName: {File} — {ErrorReason}",
            id, file.FileName, errorReason);

        _auditLogger.RecordEvent(
            eventType: "FileRestored",
            entityId: file.Id.ToString(),
            actor: "System",
            outcome: "Failed",
            details: new { Error = auditError, Details = auditDetails });
    }
}
