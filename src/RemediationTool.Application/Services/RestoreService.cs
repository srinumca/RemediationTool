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
        var file = _repository.GetById(id);
        if (file == null)
        {
            _logger.LogError(
                "[RESTORE_NOT_FOUND] FileId:{FileId}, Message:No matching record found.",
                id);
            return;
        }

        await RestoreFileAsync(file);
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetAll()
            .Where(file =>
                file.Status == FileStatus.QuarantineComplete
                || file.Status == FileStatus.PendingRestore)
            .ToList();

        _logger.LogInformation(
            "[RESTORE_ALL_START] EligibleCount:{EligibleCount}",
            files.Count);

        var succeeded = 0;
        var failed = 0;

        foreach (var file in files)
        {
            _logger.LogInformation(
                "[RESTORE_ALL_ITEM_START] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, Status:{Status}",
                file.Id,
                file.SourceRecordId,
                file.FileName,
                file.Status);

            var finalStatus = await RestoreFileAsync(file);
            if (finalStatus == FileStatus.RestorationComplete)
            {
                succeeded++;
                _logger.LogInformation(
                    "[RESTORE_ALL_ITEM_COMPLETE] FileId:{FileId}, FinalStatus:{FinalStatus}",
                    file.Id,
                    finalStatus);
            }
            else if (finalStatus == FileStatus.Error)
            {
                failed++;
                _logger.LogWarning(
                    "[RESTORE_ALL_ITEM_FAILED] FileId:{FileId}, FinalStatus:{FinalStatus}, ErrorCategory:{ErrorCategory}, ErrorReason:{ErrorReason}",
                    file.Id,
                    finalStatus,
                    file.ErrorCategory,
                    file.ErrorReason);
            }
            else
            {
                _logger.LogInformation(
                    "[RESTORE_ALL_ITEM_SKIPPED_OR_UNCHANGED] FileId:{FileId}, FinalStatus:{FinalStatus}",
                    file.Id,
                    finalStatus);
            }
        }

        if (succeeded > 0 && failed > 0)
        {
            _logger.LogWarning(
                "[RESTORE_ALL_PARTIAL_FAILURE] Succeeded:{Succeeded}, Failed:{Failed}, ErrorCategory:{ErrorCategory}",
                succeeded,
                failed,
                ErrorCategoryResolver.PartialRestoreFailure());
        }

        _logger.LogInformation(
            "[RESTORE_ALL_COMPLETE] Processed:{Processed}, Succeeded:{Succeeded}, Failed:{Failed}",
            files.Count,
            succeeded,
            failed);
    }

    private async Task<FileStatus> RestoreFileAsync(FileFinding file)
    {
        _logger.LogInformation("[RESTORE_START] FileId:{FileId}", file.Id);

        _logger.LogInformation(
            "[RESTORE_RECORD_LOADED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, Status:{Status}, FindingType:{FindingType}, CurrentFileLocation:{CurrentFileLocation}, OriginalFileLocation:{OriginalFileLocation}",
            file.Id,
            file.SourceRecordId,
            file.FileName,
            file.Status,
            file.FindingType,
            file.CurrentFileLocation,
            file.OriginalFileLocation);

        if (file.Status == FileStatus.RestorationComplete)
        {
            file.ErrorCategory = ErrorCategoryResolver.DuplicateRestoreAttempt().ToString();
            file.ErrorReason = "Restore skipped because the file is already restored.";
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogWarning(
                "[RESTORE_DUPLICATE] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, Status:{Status}",
                file.Id,
                file.SourceRecordId,
                file.FileName,
                file.Status);

            return file.Status;
        }

        if (file.Status != FileStatus.QuarantineComplete
            && file.Status != FileStatus.PendingRestore)
        {
            _logger.LogWarning(
                "[RESTORE_SKIPPED_INVALID_STATUS] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, CurrentStatus:{Status}, ExpectedStatuses:{ExpectedStatuses}",
                file.Id,
                file.SourceRecordId,
                file.FileName,
                file.Status,
                "QuarantineComplete, PendingRestore");

            return file.Status;
        }

        // Capture paths before changing status because QuarantinePath can be status-sensitive.
        var quarantinePath = ResolveQuarantinePath(file);
        var originalPath = file.OriginalFileLocation;
        var stubPath = string.IsNullOrWhiteSpace(originalPath)
            ? string.Empty
            : _fileService.BuildStubPath(originalPath);

        _logger.LogInformation(
            "[RESTORE_PATHS_RESOLVED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}, StubPath:{StubPath}",
            file.Id,
            file.SourceRecordId,
            file.FileName,
            quarantinePath,
            originalPath,
            stubPath);

        try
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                MarkFailed(
                    file,
                    "Restore target path is missing.",
                    ErrorCategoryResolver.TargetPathMissing(),
                    "Restore target path is missing",
                    new { file.FileName });
                return file.Status;
            }

            if (string.IsNullOrWhiteSpace(quarantinePath)
                || !await _fileService.ExistsAsync(quarantinePath))
            {
                MarkFailed(
                    file,
                    $"Quarantine file not found: {quarantinePath}",
                    ErrorCategoryResolver.QuarantineFileMissing(),
                    "Quarantine file not found",
                    new { file.FileName, quarantinePath });
                return file.Status;
            }

            if (await _fileService.ExistsAsync(originalPath))
            {
                _logger.LogWarning(
                    "[RESTORE_TARGET_ALREADY_EXISTS] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, TargetPath:{TargetPath}, ErrorCategory:{ErrorCategory}",
                    file.Id,
                    file.SourceRecordId,
                    file.FileName,
                    originalPath,
                    ErrorCategoryResolver.RestoreTargetConflict());
            }

            var previousStatus = file.Status;
            file.Status = FileStatus.InProgress;
            file.UpdatedDate = DateTime.UtcNow;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            _repository.Update(file);

            _logger.LogInformation(
                "[RESTORE_STATUS_UPDATED] FileId:{FileId}, PreviousStatus:{PreviousStatus}, NewStatus:{NewStatus}",
                file.Id,
                previousStatus,
                file.Status);

            await _fileService.CopyAsync(quarantinePath, originalPath);

            _logger.LogInformation(
                "[RESTORE_COPY_COMPLETE] FileId:{FileId}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}",
                file.Id,
                quarantinePath,
                originalPath);

            await _fileService.DeleteSourceAsync(quarantinePath);

            _logger.LogInformation(
                "[RESTORE_QUARANTINE_DELETE_COMPLETE] FileId:{FileId}, QuarantinePath:{QuarantinePath}",
                file.Id,
                quarantinePath);

            if (!string.IsNullOrWhiteSpace(stubPath))
            {
                if (await _fileService.ExistsAsync(stubPath))
                {
                    await _fileService.DeleteSourceAsync(stubPath);
                    _logger.LogInformation(
                        "[RESTORE_STUB_DELETE_COMPLETE] FileId:{FileId}, StubPath:{StubPath}",
                        file.Id,
                        stubPath);
                }
                else
                {
                    _logger.LogInformation(
                        "[RESTORE_STUB_NOT_FOUND] FileId:{FileId}, StubPath:{StubPath}",
                        file.Id,
                        stubPath);
                }
            }
            else
            {
                _logger.LogInformation(
                    "[RESTORE_STUB_SKIPPED] FileId:{FileId}, Reason:Stub path is empty.",
                    file.Id);
            }

            previousStatus = file.Status;
            var restoredAtUtc = DateTime.UtcNow;
            file.Status = FileStatus.RestorationComplete;
            file.RestoredDateUtc = restoredAtUtc;
            file.CurrentFileLocation = originalPath;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            file.UpdatedDate = restoredAtUtc;
            _repository.Update(file);

            _logger.LogInformation(
                "[RESTORE_COMPLETE] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, PreviousStatus:{PreviousStatus}, NewStatus:{NewStatus}, RestoredToPath:{OriginalPath}, QuarantinePath:{QuarantinePath}, StubPath:{StubPath}",
                file.Id,
                file.SourceRecordId,
                file.FileName,
                previousStatus,
                file.Status,
                originalPath,
                quarantinePath,
                stubPath);

            _auditLogger.RecordEvent(
                eventType: "FileRestored",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Success",
                details: new
                {
                    file.FileName,
                    file.SourceRecordId,
                    RestoredToPath = originalPath,
                    QuarantinePath = quarantinePath,
                    StubPath = stubPath,
                    file.FileOwner,
                    file.SiteOwner,
                    file.LastModifiedDateUtc,
                    file.CreatedDateUtc,
                    file.LastAccessedDateUtc,
                    file.PolicyName,
                    file.PolicyId,
                    file.SensitivityLabel
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

            _logger.LogError(
                ex,
                "[RESTORE_FAILED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, Error:{Error}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}, StubPath:{StubPath}",
                file.Id,
                file.SourceRecordId,
                file.FileName,
                category,
                ex.Message,
                quarantinePath,
                originalPath,
                stubPath);

            _auditLogger.RecordEvent(
                eventType: "FileRestored",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Failed",
                details: new
                {
                    file.FileName,
                    file.SourceRecordId,
                    Error = ex.Message,
                    ErrorCategory = category.ToString(),
                    QuarantinePath = quarantinePath,
                    OriginalPath = originalPath,
                    StubPath = stubPath,
                    file.FileOwner,
                    file.SiteOwner,
                    file.LastModifiedDateUtc,
                    file.CreatedDateUtc,
                    file.LastAccessedDateUtc
                });
        }

        return file.Status;
    }

    private static string? ResolveQuarantinePath(FileFinding file)
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
        string auditError,
        object auditDetails)
    {
        file.Status = FileStatus.Error;
        file.ErrorReason = errorReason;
        file.ErrorCategory = errorCategory.ToString();
        file.UpdatedDate = DateTime.UtcNow;
        _repository.Update(file);

        _logger.LogError(
            "[RESTORE_FAILED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, ErrorReason:{ErrorReason}",
            file.Id,
            file.SourceRecordId,
            file.FileName,
            errorCategory,
            errorReason);

        _auditLogger.RecordEvent(
            eventType: "FileRestored",
            entityId: file.Id.ToString(),
            actor: "System",
            outcome: "Failed",
            details: new { Error = auditError, Details = auditDetails });
    }
}
