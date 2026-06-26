using Microsoft.Extensions.Logging;
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

    public RestoreService(IFileFindingRepository repository, ILogger<RestoreService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RestoreAsync(Guid id)
    {
        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogError("File not found: {Id}", id);
            return;
        }

        if (file.Status != FileStatus.QuarantineComplete &&
            file.Status != FileStatus.PendingRestore)
        {
            _logger.LogWarning("File is not in a restorable state: {File} Status: {Status}",
                file.FileName, file.Status);
            return;
        }

        try
        {
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
                _logger.LogError("Quarantine file missing: {Path}", quarantinePath);
                return;
            }

            var dir = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.Copy(quarantinePath, originalPath, overwrite: true);
            File.Delete(quarantinePath);

            var stubPath = originalPath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
                File.Delete(stubPath);

            file.Status = FileStatus.RestorationComplete;
            file.RestoredDateUtc = DateTime.UtcNow;
            file.QuarantinePath = null;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation("File restored: {File}", file.FileName);
        }
        catch (Exception ex)
        {
            file.Status = FileStatus.Error;
            file.ErrorReason = ex.Message;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);
            _logger.LogError(ex, "Restore failed: {File}", file.FileName);
        }
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete ||
                        x.Status == FileStatus.PendingRestore)
            .ToList();

        foreach (var file in files)
            await RestoreAsync(file.Id);
    }
}