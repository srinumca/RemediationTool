using Microsoft.Extensions.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Services;

/// <summary>
/// Quarantine service — moves eligible files to quarantine.
/// Uses FileStatus enum (not FindingType) for workflow state.
/// </summary>
public class QuarantineService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<QuarantineService> _logger;

    private const int RetentionYears = 10;

    private readonly string _basePath;
    private readonly string _sourceFolder;
    private readonly string _quarantineFolder;

    public QuarantineService(IFileFindingRepository repository, ILogger<QuarantineService> logger)
    {
        _repository = repository;
        _logger = logger;

        _basePath = Path.Combine(Directory.GetCurrentDirectory(), "storage");
        _sourceFolder = Path.Combine(_basePath, "source");
        _quarantineFolder = Path.Combine(_basePath, "quarantine");

        Directory.CreateDirectory(_sourceFolder);
        Directory.CreateDirectory(_quarantineFolder);
    }

    public async Task ProcessAsync()
    {
        _logger.LogInformation("=== Quarantine Job Started ===");

        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.PendingQuarantine)
            .ToList();

        _logger.LogInformation("Files pending quarantine: {Count}", files.Count);

        foreach (var file in files)
        {
            try
            {
                _logger.LogInformation("Processing: {File}", file.FileName);

                // Retention check
                if (file.LastModifiedDate > DateTime.UtcNow.AddYears(-RetentionYears))
                {
                    file.Status = FileStatus.NotYetStarted;
                    file.UpdatedDate = DateTime.UtcNow;
                    _repository.Update(file);
                    _logger.LogWarning("Skipped (retention not met): {File}", file.FileName);
                    continue;
                }

                var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), file.FilePath);

                if (!File.Exists(sourcePath))
                {
                    file.Status = FileStatus.Error;
                    file.ErrorReason = $"File not found at source: {sourcePath}";
                    file.UpdatedDate = DateTime.UtcNow;
                    _repository.Update(file);
                    _logger.LogError("File not found: {Path}", sourcePath);
                    continue;
                }

                // Mark in progress
                file.Status = FileStatus.InProgress;
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);

                var fileName = Path.GetFileName(sourcePath);
                var quarantinePath = Path.Combine(_quarantineFolder, fileName);

                File.Copy(sourcePath, quarantinePath, overwrite: true);
                File.Delete(sourcePath);

                // Create stub
                File.WriteAllText(sourcePath + "_Retention_Placeholder",
                    "This file was moved due to ADP retention policy. Contact admin to request restore.");

                // Update to complete
                file.Status = FileStatus.QuarantineComplete;
                file.QuarantinePath = quarantinePath;
                file.QuarantineDate = DateTime.UtcNow;
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogInformation("Quarantined: {File}", file.FileName);
            }
            catch (Exception ex)
            {
                file.Status = FileStatus.Error;
                file.ErrorReason = ex.Message;
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);
                _logger.LogError(ex, "Quarantine failed: {File}", file.FileName);
            }
        }

        _logger.LogInformation("=== Quarantine Job Completed ===");
        await Task.CompletedTask;
    }
}