using Microsoft.Extensions.Logging;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Services;

/// <summary>
/// Delete service — permanently deletes quarantined files.
/// Uses FileStatus enum for workflow state.
/// </summary>
public class DeleteService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<DeleteService> _logger;
    private readonly IAuditLogger _auditLogger;

    public DeleteService(
        IFileFindingRepository repository,
        ILogger<DeleteService> logger,
        IAuditLogger auditLogger)
    {
        _repository = repository;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task DeleteAsync(Guid id)
    {
        _logger.LogInformation("[DELETE START] FileId: {Id}", id);

        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogWarning("[DELETE NOT FOUND] FileId: {Id} — no matching record.", id);
            return;
        }

        if (file.Status != FileStatus.QuarantineComplete)
        {
            _logger.LogWarning(
                "[DELETE SKIPPED] FileId: {Id} FileName: {File} — file is not in QuarantineComplete state (current: {Status}).",
                id, file.FileName, file.Status);
            return;
        }

        try
        {
            file.Status = FileStatus.InProgress;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            var quarantinePath = file.QuarantinePath;

            if (!string.IsNullOrWhiteSpace(quarantinePath) && File.Exists(quarantinePath))
            {
                File.Delete(quarantinePath);
                _logger.LogInformation(
                    "[DELETE FILE] FileId: {Id} FileName: {File} — quarantined file deleted at {Path}.",
                    id, file.FileName, quarantinePath);
            }

            var stubPath = file.FilePath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
            {
                File.Delete(stubPath);
                _logger.LogInformation(
                    "[DELETE STUB] FileId: {Id} FileName: {File} — retention placeholder removed at {StubPath}.",
                    id, file.FileName, stubPath);
            }

            file.Status = FileStatus.DeletionComplete;
            file.QuarantinePath = null;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation(
                "[DELETE COMPLETE] FileId: {Id} FileName: {File} — deleted permanently.",
                id, file.FileName);

            // Audit event — irreversible action, must be traceable for compliance.
            _auditLogger.RecordEvent(
                eventType: "FileDeleted",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Success",
                details: new { file.FileName, DeletedAtUtc = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            file.Status = FileStatus.Error;
            file.ErrorReason = ex.Message;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogError(ex,
                "[DELETE FAILED] FileId: {Id} FileName: {File} — delete failed. Error: {Error}",
                id, file.FileName, ex.Message);

            _auditLogger.RecordEvent(
                eventType: "FileDeleted",
                entityId: file.Id.ToString(),
                actor: "System",
                outcome: "Failed",
                details: new { file.FileName, Error = ex.Message });
        }

        await Task.CompletedTask;
    }

    public async Task DeleteAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete)
            .ToList();

        _logger.LogInformation("[DELETE ALL START] Found {Count} file(s) eligible for deletion.", files.Count);

        foreach (var file in files)
            await DeleteAsync(file.Id);

        _logger.LogInformation("[DELETE ALL COMPLETE] Processed {Count} file(s).", files.Count);
    }
}