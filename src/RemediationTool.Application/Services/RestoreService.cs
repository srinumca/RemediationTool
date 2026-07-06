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
        _logger.LogInformation("[RESTORE_START] FileId:{FileId}", id);

        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogError("[RESTORE_NOT_FOUND] FileId:{FileId}, Message:No matching record found.", id);
            return;
        }

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
                id,
                file.SourceRecordId,
                file.FileName,
                file.Status);
            return;
        }

        if (file.Status != FileStatus.QuarantineComplete &&
            file.Status != FileStatus.PendingRestore)
        {
            _logger.LogWarning(
                "[RESTORE_SKIPPED_INVALID_STATUS] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, CurrentStatus:{Status}, ExpectedStatuses:{ExpectedStatuses}",
                id,
                file.SourceRecordId,
                file.FileName,
                file.Status,
                "QuarantineComplete, PendingRestore");
            return;
        }

        // Capture paths before changing Status to InProgress.
        // FileFinding.QuarantinePath returns a value only when Status == QuarantineComplete.
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
                _logger.LogError(
                    "[RESTORE_VALIDATION_FAILED] FileId:{FileId}, FileName:{FileName}, Reason:Original restore path is missing.",
                    file.Id,
                    file.FileName);

                MarkFailed(
                    file,
                    "Restore target path is missing.",
                    ErrorCategoryResolver.TargetPathMissing(),
                    id,
                    "Restore target path is missing",
                    new { file.FileName });
                return;
            }

            _logger.LogInformation(
                "[RESTORE_QUARANTINE_EXISTS_CHECK_START] FileId:{FileId}, QuarantinePath:{QuarantinePath}",
                file.Id,
                quarantinePath);

            if (string.IsNullOrWhiteSpace(quarantinePath) ||
                !await _fileService.ExistsAsync(quarantinePath))
            {
                _logger.LogError(
                    "[RESTORE_VALIDATION_FAILED] FileId:{FileId}, FileName:{FileName}, Reason:Quarantine file missing, QuarantinePath:{QuarantinePath}",
                    file.Id,
                    file.FileName,
                    quarantinePath);

                MarkFailed(
                    file,
                    $"Quarantine file not found: {quarantinePath}",
                    ErrorCategoryResolver.QuarantineFileMissing(),
                    id,
                    "Quarantine file not found",
                    new { file.FileName, quarantinePath });
                return;
            }

            _logger.LogInformation(
                "[RESTORE_QUARANTINE_EXISTS_CHECK_COMPLETE] FileId:{FileId}, QuarantinePath:{QuarantinePath}, Exists:true",
                file.Id,
                quarantinePath);

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

            _logger.LogInformation(
                "[RESTORE_COPY_START] FileId:{FileId}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}",
                file.Id,
                quarantinePath,
                originalPath);

            await _fileService.CopyAsync(quarantinePath, originalPath);

            _logger.LogInformation(
                "[RESTORE_COPY_COMPLETE] FileId:{FileId}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}",
                file.Id,
                quarantinePath,
                originalPath);

            _logger.LogInformation(
                "[RESTORE_QUARANTINE_DELETE_START] FileId:{FileId}, QuarantinePath:{QuarantinePath}",
                file.Id,
                quarantinePath);

            await _fileService.DeleteSourceAsync(quarantinePath);

            _logger.LogInformation(
                "[RESTORE_QUARANTINE_DELETE_COMPLETE] FileId:{FileId}, QuarantinePath:{QuarantinePath}",
                file.Id,
                quarantinePath);

            if (!string.IsNullOrWhiteSpace(stubPath))
            {
                _logger.LogInformation(
                    "[RESTORE_STUB_EXISTS_CHECK_START] FileId:{FileId}, StubPath:{StubPath}",
                    file.Id,
                    stubPath);

                if (await _fileService.ExistsAsync(stubPath))
                {
                    _logger.LogInformation(
                        "[RESTORE_STUB_DELETE_START] FileId:{FileId}, StubPath:{StubPath}",
                        file.Id,
                        stubPath);

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
            file.Status = FileStatus.RestorationComplete;
            file.RestoredDateUtc = DateTime.UtcNow;
            file.CurrentFileLocation = originalPath;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation(
                "[RESTORE_STATUS_UPDATED] FileId:{FileId}, PreviousStatus:{PreviousStatus}, NewStatus:{NewStatus}, RestoredDateUtc:{RestoredDateUtc}",
                file.Id,
                previousStatus,
                file.Status,
                file.RestoredDateUtc);

            _logger.LogInformation(
                "[RESTORE_COMPLETE] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, RestoredToPath:{OriginalPath}, QuarantinePath:{QuarantinePath}, StubPath:{StubPath}",
                id,
                file.SourceRecordId,
                file.FileName,
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

            _logger.LogError(ex,
                "[RESTORE_FAILED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, Error:{Error}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}, StubPath:{StubPath}",
                id,
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
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete ||
                        x.Status == FileStatus.PendingRestore)
            .ToList();

        _logger.LogInformation("[RESTORE_ALL_START] EligibleCount:{EligibleCount}", files.Count);

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

            await RestoreAsync(file.Id);

            var updated = _repository.GetById(file.Id);
            if (updated?.Status == FileStatus.RestorationComplete)
            {
                succeeded++;
                _logger.LogInformation(
                    "[RESTORE_ALL_ITEM_COMPLETE] FileId:{FileId}, FinalStatus:{FinalStatus}",
                    file.Id,
                    updated.Status);
            }
            else if (updated?.Status == FileStatus.Error)
            {
                failed++;
                _logger.LogWarning(
                    "[RESTORE_ALL_ITEM_FAILED] FileId:{FileId}, FinalStatus:{FinalStatus}, ErrorCategory:{ErrorCategory}, ErrorReason:{ErrorReason}",
                    file.Id,
                    updated.Status,
                    updated.ErrorCategory,
                    updated.ErrorReason);
            }
            else
            {
                _logger.LogInformation(
                    "[RESTORE_ALL_ITEM_SKIPPED_OR_UNCHANGED] FileId:{FileId}, FinalStatus:{FinalStatus}",
                    file.Id,
                    updated?.Status.ToString() ?? "RecordNotFound");
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
            "[RESTORE_FAILED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, ErrorReason:{ErrorReason}",
            id,
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
