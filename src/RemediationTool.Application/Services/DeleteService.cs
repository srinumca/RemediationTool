using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Delete service — permanently deletes quarantined files.
/// Uses the configured quarantine file service so delete works for both local and S3 storage.
/// </summary>
public class DeleteService
{
    private readonly IFileFindingRepository _repository;
    private readonly IQuarantineFileService _fileService;
    private readonly QuarantineProcessingOptions _options;
    private readonly ILogger<DeleteService> _logger;
    private readonly IAuditLogger _auditLogger;

    public DeleteService(
        IFileFindingRepository repository,
        IQuarantineFileService fileService,
        IOptions<QuarantineProcessingOptions> options,
        ILogger<DeleteService> logger,
        IAuditLogger auditLogger)
    {
        _repository = repository;
        _fileService = fileService;
        _options = options.Value;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task DeleteAsync(Guid id)
    {
        _logger.LogInformation("[DELETE_START] FileId:{FileId}", id);

        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogWarning("[DELETE_NOT_FOUND] FileId:{FileId}, Message:No matching record found.", id);
            return;
        }

        if (file.Status == FileStatus.DeletionComplete)
        {
            file.ErrorCategory = ErrorCategoryResolver.DuplicateDeleteAttempt().ToString();
            file.ErrorReason = "Delete skipped because the file is already deleted.";
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogWarning(
                "[DELETE_DUPLICATE] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, Status:{Status}",
                id,
                file.SourceRecordId,
                file.FileName,
                file.Status);
            return;
        }

        if (file.Status != FileStatus.QuarantineComplete)
        {
            _logger.LogWarning(
                "[DELETE_SKIPPED_INVALID_STATUS] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, CurrentStatus:{Status}, ExpectedStatus:{ExpectedStatus}",
                id,
                file.SourceRecordId,
                file.FileName,
                file.Status,
                FileStatus.QuarantineComplete);
            return;
        }

        var quarantinePath = file.QuarantinePath;
        var originalPath = file.OriginalFileLocation;
        var stubPath = string.IsNullOrWhiteSpace(originalPath)
            ? string.Empty
            : _fileService.BuildStubPath(originalPath);

        _logger.LogInformation(
            "[DELETE_PATHS_RESOLVED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, QuarantinePath:{QuarantinePath}, OriginalPath:{OriginalPath}, StubPath:{StubPath}",
            file.Id,
            file.SourceRecordId,
            file.FileName,
            quarantinePath,
            originalPath,
            stubPath);

        try
        {
            if (string.IsNullOrWhiteSpace(quarantinePath))
            {
                MarkFailed(
                    file,
                    "Delete failed because quarantine path is missing.",
                    ErrorCategoryResolver.DeleteQuarantineFileMissing(),
                    id,
                    "Quarantine path missing",
                    new { file.FileName, file.SourceRecordId });
                return;
            }

            if (!await _fileService.ExistsAsync(quarantinePath))
            {
                MarkFailed(
                    file,
                    $"Delete failed because quarantine file was not found: {quarantinePath}",
                    ErrorCategoryResolver.DeleteQuarantineFileMissing(),
                    id,
                    "Quarantine file missing",
                    new { file.FileName, file.SourceRecordId, quarantinePath });
                return;
            }

            var previousStatus = file.Status;
            file.Status = FileStatus.DeletionInProgress;
            file.UpdatedDate = DateTime.UtcNow;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            _repository.Update(file);

            _logger.LogInformation(
                "[DELETE_STATUS_UPDATED] FileId:{FileId}, PreviousStatus:{PreviousStatus}, NewStatus:{NewStatus}",
                file.Id,
                previousStatus,
                file.Status);

            _logger.LogInformation(
                "[DELETE_QUARANTINE_FILE_START] FileId:{FileId}, QuarantinePath:{QuarantinePath}",
                file.Id,
                quarantinePath);

            await _fileService.DeleteSourceAsync(quarantinePath);

            _logger.LogInformation(
                "[DELETE_QUARANTINE_FILE_COMPLETE] FileId:{FileId}, QuarantinePath:{QuarantinePath}",
                file.Id,
                quarantinePath);

            if (!string.IsNullOrWhiteSpace(stubPath))
            {
                _logger.LogInformation(
                    "[DELETE_STUB_EXISTS_CHECK_START] FileId:{FileId}, StubPath:{StubPath}",
                    file.Id,
                    stubPath);

                if (await _fileService.ExistsAsync(stubPath))
                {
                    await _fileService.DeleteSourceAsync(stubPath);
                    _logger.LogInformation(
                        "[DELETE_STUB_DELETE_COMPLETE] FileId:{FileId}, StubPath:{StubPath}",
                        file.Id,
                        stubPath);
                }
                else
                {
                    _logger.LogInformation(
                        "[DELETE_STUB_NOT_FOUND] FileId:{FileId}, StubPath:{StubPath}",
                        file.Id,
                        stubPath);
                }
            }

            previousStatus = file.Status;
            file.Status = FileStatus.DeletionComplete;
            file.DeletedDateUtc = DateTime.UtcNow;
            file.CurrentFileLocation = string.Empty;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.ErrorReason = string.Empty;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation(
                "[DELETE_COMPLETE] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, PreviousStatus:{PreviousStatus}, NewStatus:{NewStatus}, DeletedDateUtc:{DeletedDateUtc}",
                id,
                file.SourceRecordId,
                file.FileName,
                previousStatus,
                file.Status,
                file.DeletedDateUtc);

            _auditLogger.RecordEvent(
                eventType: "FileDeleted",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Success",
                details: new
                {
                    file.FileName,
                    file.SourceRecordId,
                    DeletedAtUtc = file.DeletedDateUtc,
                    QuarantinePath = quarantinePath,
                    StubPath = stubPath,
                    OriginalPath = originalPath,
                    file.FileOwner,
                    file.SiteOwner,
                    file.LastModifiedDateUtc,
                    file.CreatedDateUtc
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
                "[DELETE_FAILED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, Error:{Error}, QuarantinePath:{QuarantinePath}, StubPath:{StubPath}",
                id,
                file.SourceRecordId,
                file.FileName,
                category,
                ex.Message,
                quarantinePath,
                stubPath);

            _auditLogger.RecordEvent(
                eventType: "FileDeleted",
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
                    StubPath = stubPath,
                    OriginalPath = originalPath
                });
        }
    }

    public async Task DeleteAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete)
            .ToList();

        _logger.LogInformation("[DELETE_ALL_START] Found {Count} file(s) eligible for deletion.", files.Count);

        var succeeded = 0;
        var failed = 0;

        foreach (var file in files)
        {
            await DeleteAsync(file.Id);

            var updated = _repository.GetById(file.Id);
            if (updated?.Status == FileStatus.DeletionComplete)
                succeeded++;
            else if (updated?.Status == FileStatus.Error)
                failed++;
        }

        if (succeeded > 0 && failed > 0)
        {
            _logger.LogWarning(
                "[DELETE_ALL_PARTIAL_FAILURE] Succeeded:{Succeeded}, Failed:{Failed}, ErrorCategory:{ErrorCategory}",
                succeeded,
                failed,
                ErrorCategoryResolver.PartialDeleteFailure());
        }

        _logger.LogInformation(
            "[DELETE_ALL_COMPLETE] Processed:{Processed}, Succeeded:{Succeeded}, Failed:{Failed}",
            files.Count,
            succeeded,
            failed);
    }

    public async Task<int> DeleteRetentionEligibleAsync(DateTime? asOfUtc = null)
    {
        var cutoffUtc = (asOfUtc ?? DateTime.UtcNow).AddYears(-Math.Max(0, _options.RetentionYears));

        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete)
            .Where(x => x.QuarantineDateUtc.HasValue && x.QuarantineDateUtc.Value <= cutoffUtc)
            .ToList();

        _logger.LogInformation(
            "[DELETE_RETENTION_START] RetentionYears:{RetentionYears}, CutoffUtc:{CutoffUtc}, EligibleCount:{EligibleCount}",
            _options.RetentionYears,
            cutoffUtc,
            files.Count);

        var deletedCount = 0;

        foreach (var file in files)
        {
            await DeleteAsync(file.Id);

            var updated = _repository.GetById(file.Id);
            if (updated?.Status == FileStatus.DeletionComplete)
                deletedCount++;
        }

        _logger.LogInformation(
            "[DELETE_RETENTION_COMPLETE] EligibleCount:{EligibleCount}, DeletedCount:{DeletedCount}",
            files.Count,
            deletedCount);

        return deletedCount;
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
            "[DELETE_FAILED] FileId:{FileId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, ErrorReason:{ErrorReason}",
            id,
            file.SourceRecordId,
            file.FileName,
            errorCategory,
            errorReason);

        _auditLogger.RecordEvent(
            eventType: "FileDeleted",
            entityId: file.Id.ToString(),
            actor: "System",
            outcome: "Failed",
            details: new { Error = auditError, Details = auditDetails });
    }
}
