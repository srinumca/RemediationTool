using Microsoft.Extensions.Logging;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;

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
        _logger.LogDebug("RestoreAsync invoked for FileId: {Id}", id);
        _logger.LogInformation("[RESTORE START] FileId: {Id}", id);

        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogError("[RESTORE NOT FOUND] FileId: {Id} — no matching record.", id);
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

        try
        {
            _logger.LogDebug("Restoring FileId: {Id}, FileName: {FileName}, QuarantinePath: {QuarantinePath}, OriginalPath: {OriginalPath}",
                id, file.FileName, file.QuarantinePath, file.OriginalFileLocation ?? file.FilePath);

            file.Status = FileStatus.InProgress;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            var quarantinePath = file.QuarantinePath;
            var originalPath = file.OriginalFileLocation ?? file.FilePath;

            if (string.IsNullOrWhiteSpace(quarantinePath) || !File.Exists(quarantinePath))
            {
                file.Status = FileStatus.Error;
                file.ErrorReason = $"Quarantine file not found: {quarantinePath}";
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
            file.Status = FileStatus.Error;
            file.ErrorReason = ex.Message;
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
                details: new { file.FileName, Error = ex.Message });
        }
    }

    public async Task RestoreAllAsync()
    {
        _logger.LogInformation("[RESTORE ALL START] Fetching all files eligible for restore.");

        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete ||
                        x.Status == FileStatus.PendingRestore)
            .ToList();

        _logger.LogInformation("[RESTORE ALL START] Found {Count} file(s) eligible for restore.", files.Count);

        foreach (var file in files)
        {
            _logger.LogDebug("Processing batch restore for FileId: {Id}, FileName: {FileName}", file.Id, file.FileName);
            await RestoreAsync(file.Id);
        }

        _logger.LogInformation("[RESTORE ALL COMPLETE] Processed {Count} file(s).", files.Count);
    }
}