using Microsoft.Extensions.Logging;
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

    public DeleteService(IFileFindingRepository repository, ILogger<DeleteService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task DeleteAsync(Guid id)
    {
        var file = _repository.GetAll().FirstOrDefault(x => x.Id == id);

        if (file == null)
        {
            _logger.LogWarning("File not found for delete: {Id}", id);
            return;
        }

        if (file.Status != FileStatus.QuarantineComplete)
        {
            _logger.LogWarning("File is not in QuarantineComplete state: {File}", file.FileName);
            return;
        }

        try
        {
            file.Status = FileStatus.InProgress;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            var quarantinePath = file.QuarantinePath;

            if (!string.IsNullOrWhiteSpace(quarantinePath) && File.Exists(quarantinePath))
                File.Delete(quarantinePath);

            var stubPath = file.FilePath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
                File.Delete(stubPath);

            file.Status = FileStatus.DeletionComplete;
            file.QuarantinePath = null;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation("File deleted permanently: {File}", file.FileName);
        }
        catch (Exception ex)
        {
            file.Status = FileStatus.Error;
            file.ErrorReason = ex.Message;
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);
            _logger.LogError(ex, "Delete failed: {File}", file.FileName);
        }

        await Task.CompletedTask;
    }

    public async Task DeleteAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.QuarantineComplete)
            .ToList();

        foreach (var file in files)
            await DeleteAsync(file.Id);
    }
}