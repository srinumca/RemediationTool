using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Repositories;
using Microsoft.Extensions.Logging;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Services;

public class RestoreService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<RestoreService> _logger;

    public RestoreService(IFileFindingRepository repository,
                          ILogger<RestoreService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RestoreAsync(Guid fileId)
    {
        var file = _repository.GetAll().FirstOrDefault(x => x.Id == fileId);

        if (file == null)
        {
            _logger.LogError("File not found in metadata");
            return;
        }

        if (file.Status != FileStatus.Quarantined)
        {
            _logger.LogWarning("File is not in Quarantined state");
            return;
        }

        try
        {
            var originalPath = Path.Combine(Directory.GetCurrentDirectory(), file.FilePath);
            var quarantinePath = file.QuarantinePath;

            if (!File.Exists(quarantinePath))
            {
                _logger.LogError("Quarantine file missing: {Path}", quarantinePath); 
                return;
            }  

            // Restore file
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);

            File.Copy(quarantinePath, originalPath, true);

            // FIX: delete from quarantine
            File.Delete(quarantinePath);

            // Delete stub
            var stubPath = originalPath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
            {
                File.Delete(stubPath);
            }

            // Update metadata
            file.Status = FileStatus.Restored;
            file.QuarantinePath = null;
            file.UpdatedDate = DateTime.UtcNow;

            _repository.Update(file);

            _logger.LogInformation("File restored: {File}", file.FileName);
        }
        catch (Exception ex)
        {
            file.Status = FileStatus.Failed;
            _repository.Update(file);

            _logger.LogError(ex, "Restore failed: {File}", file.FileName);
        }
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.Quarantined)
            .ToList();

        foreach (var file in files)
        {
            await RestoreAsync(file.Id);
        }
    }
}