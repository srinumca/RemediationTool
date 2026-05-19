using RemediationTool.Application.Interfaces;
using RemediationTool.Domain;
using Microsoft.Extensions.Logging;

namespace RemediationTool.Application.Services;

public class DeleteService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<DeleteService> _logger;

    public DeleteService(IFileFindingRepository repository,
                         ILogger<DeleteService> logger)
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

        if (file.Status != FileStatus.Quarantined)
        {
            _logger.LogWarning("File is not in Quarantined state: {File}", file.FileName);
            return;
        }

        try
        {
            var originalPath = Path.Combine(Directory.GetCurrentDirectory(), file.FilePath);
            var quarantinePath = file.QuarantinePath;

            // 🔹 Delete from quarantine
            if (!string.IsNullOrWhiteSpace(quarantinePath) && File.Exists(quarantinePath))
            {
                File.Delete(quarantinePath);
            }

            // 🔹 Delete stub
            var stubPath = originalPath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
            {
                File.Delete(stubPath);
            }

            // 🔹 Update metadata
            file.Status = FileStatus.Deleted;
            file.QuarantinePath = null;
            file.UpdatedDate = DateTime.UtcNow;

            _repository.Update(file);

            _logger.LogInformation("File deleted permanently: {File}", file.FileName);
        }
        catch (Exception ex)
        {
            file.Status = FileStatus.Failed;
            _repository.Update(file);

            _logger.LogError(ex, "Delete failed: {File}", file.FileName);
        }

        await Task.CompletedTask;
    }

    public async Task DeleteAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.Quarantined)
            .ToList();

        foreach (var file in files)
        {
            await DeleteAsync(file.Id);
        }
    }
}