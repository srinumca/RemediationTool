using Microsoft.Extensions.Logging;
using RemediationTool.Domain;

namespace RemediationTool.Application.Services;

public class QuarantineService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<QuarantineService> _logger;

    private const int RetentionYears = 10;

    private readonly string basePath;
    private readonly string sourceFolder;
    private readonly string quarantineFolder;

    public QuarantineService(IFileFindingRepository repository,
                             ILogger<QuarantineService> logger)
    {
        _repository = repository;
        _logger = logger;

        basePath = Path.Combine(Directory.GetCurrentDirectory(), "storage");
        sourceFolder = Path.Combine(basePath, "source");
        quarantineFolder = Path.Combine(basePath, "quarantine");

        // Ensure folders exist
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(quarantineFolder);
    }

    public async Task ProcessAsync()
    {
        _logger.LogInformation("=== Quarantine Job Started ===");

        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.Loaded)
            .ToList();

        _logger.LogInformation("Total files: {Count}", files.Count);

        foreach (var file in files)
        {
            try
            {
                _logger.LogInformation("Processing: {File}", file.FileName);

                // 🔹 Retention Check
                if (file.LastModifiedDate > DateTime.UtcNow.AddYears(-RetentionYears))
                {
                    file.Status = FileStatus.NotEligible;
                    _repository.Update(file);

                    _logger.LogWarning("Skipped (Retention not met): {File}", file.FileName);
                    continue;
                }

                // 🔹 Convert to full path
                var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), file.FilePath);

                if (!File.Exists(sourcePath))
                {
                    file.Status = FileStatus.Missing;
                    _repository.Update(file);

                    _logger.LogError("File not found: {Path}", sourcePath);
                    continue;
                }

                // 🔹 Prepare quarantine path
                var fileName = Path.GetFileName(sourcePath);
                var quarantinePath = Path.Combine(quarantineFolder, fileName);

                // 🔹 Move file
                File.Copy(sourcePath, quarantinePath, true);
                File.Delete(sourcePath);

                // 🔹 Create stub
                var stubPath = sourcePath + "_Retention_Placeholder";

                File.WriteAllText(stubPath, @"This file was moved due to retention policy. To request restore, contact admin.");

                // 🔹 Update metadata
                file.Status = FileStatus.Quarantined;
                file.QuarantinePath = quarantinePath;
                file.UpdatedDate = DateTime.UtcNow;

                _repository.Update(file);

                _logger.LogInformation("Quarantined successfully: {File}", file.FileName);
            }
            catch (Exception ex)
            {
                file.Status = FileStatus.Failed;
                _repository.Update(file);

                _logger.LogError(ex, "Error processing file: {File}", file.FileName);
            }
        }

        _logger.LogInformation("=== Quarantine Job Completed ===");
    }
}