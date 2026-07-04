using Microsoft.Extensions.Logging;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Restore service — restores quarantined files to their original location.
/// Uses FileStatus enum for workflow state.
/// </summary>
public class RestoreService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<RestoreService> _logger;
    private readonly IAuditLogger _auditLogger;

    public RestoreService(
        IFileFindingRepository repository,
        ILogger<RestoreService> logger,
        IAuditLogger auditLogger)
    {
        _repository = repository;
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
        // FileFinding.QuarantinePath is a compatibility property that returns a value
        // only when Status == QuarantineComplete, so reading it after setting InProgress
        // would incorrectly return null.
        var quarantinePath = file.QuarantinePath;
        var originalPath = file.OriginalFileLocation ?? file.FilePath;

        try
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                file.Status = FileStatus.Error;
                file.ErrorReason = "Restore target path is missing.";
                file.ErrorCategory = ErrorCategoryResolver.TargetPathMissing().ToString();
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogError(
                    "[RESTORE FAILED] FileId: {Id} FileName: {File} — restore target path is missing.",
                    id, file.FileName);

                _auditLogger.RecordEvent(
                    eventType: "FileRestored",
                    entityId: file.Id.ToString(),
                    actor: "System",
                    outcome: "Failed",
                    details: new { file.FileName, Error = "Restore target path is missing" });

                return;
            }

            if (string.IsNullOrWhiteSpace(quarantinePath) || !File.Exists(quarantinePath))
            {
                file.Status = FileStatus.Error;
                file.ErrorReason = $"Quarantine file not found: {quarantinePath}";
                file.ErrorCategory = ErrorCategoryResolver.QuarantineFileMissing().ToString();
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogError(
                    "[RESTORE FAILED] FileId: {Id} FileName: {File} — quarantine file missing at {Path}.",
                    id, file.FileName, quarantinePath);

                _auditLogger.RecordEvent(
                    eventType: "FileRestored",
                    entityId: file.Id.ToString(),
                    actor: "System",
                    outcome: "Failed",
                    details: new { file.FileName, Error = "Quarantine file not found", quarantinePath });

                return;
            }

            file.Status = FileStatus.InProgress;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            var dir = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.Copy(quarantinePath, originalPath, overwrite: true);
            File.Delete(quarantinePath);

            _logger.LogInformation(
                "[RESTORE FILE] FileId: {Id} FileName: {File} — copied from {QuarantinePath} to {OriginalPath}.",
                id, file.FileName, quarantinePath, originalPath);

            var stubPath = originalPath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
            {
                File.Delete(stubPath);
                _logger.LogInformation(
                    "[RESTORE STUB] FileId: {Id} FileName: {File} — retention placeholder removed at {StubPath}.",
                    id, file.FileName, stubPath);
            }

            file.Status = FileStatus.RestorationComplete;
            file.RestoredDateUtc = DateTime.UtcNow;
            file.QuarantinePath = null;
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
                details: new { file.FileName, RestoredToPath = originalPath });
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
                details: new { file.FileName, Error = ex.Message, ErrorCategory = category.ToString() });
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
}
