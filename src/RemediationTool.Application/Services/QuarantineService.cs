using Microsoft.Extensions.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// POC quarantine service — moves files from source to quarantine folder.
/// NOTE: This is placeholder POC logic. When the Automated Quarantine requirement
/// (Req 30-40) is properly implemented, this service will be fully rewritten
/// to use the spec-defined workflow: validate at source, move to centralized
/// quarantine, create breadcrumb stub, update data model (append-only).
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

        // Get all files currently in Obsolete state
        var files = _repository.GetLatestByFindingType(FindingType.Obsolete).ToList();

        _logger.LogInformation("Total obsolete files: {Count}", files.Count);

        foreach (var file in files)
        {
            try
            {
                _logger.LogInformation("Processing: {File}", file.FindingFileName);

                //// Retention check — skip if modified within the last 10 years
                //if (file.LastModifiedDateUtc.HasValue
                //    && file.LastModifiedDateUtc.Value > DateTime.UtcNow.AddYears(-RetentionYears))
                //{
                //    file.FindingType = FindingType.NotObsolete.ToString();
                //    file.LastUpdateDateUtc = DateTime.UtcNow;
                //    _repository.Update(file);

                //    _logger.LogWarning("Skipped (retention not met): {File}", file.FindingFileName);
                //    continue;
                //}

                // Resolve full source path
                var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), file.CurrentFileLocation);

                if (!File.Exists(sourcePath))
                {
                    file.ErrorCategory = ErrorCategory.MissingAtSource;
                    file.ErrorDetail = $"File not found at source path: {sourcePath}";
                    file.LastUpdateDateUtc = DateTime.UtcNow;
                    _repository.Update(file);

                    _logger.LogError("File not found: {Path}", sourcePath);
                    continue;
                }

                // Move to quarantine
                var fileName = Path.GetFileName(sourcePath);
                var quarantinePath = Path.Combine(_quarantineFolder, fileName);

                File.Copy(sourcePath, quarantinePath, overwrite: true);
                File.Delete(sourcePath);

                // Create breadcrumb stub at original location
                var stubPath = sourcePath + "_Retention_Placeholder";
                File.WriteAllText(stubPath,
                    "This file was moved to a secure location per ADP's Retention Policy. " +
                    "To request file restoration, submit a NetApp/SMB – File Restore Request.");

                // Update data model
                file.OriginalFileLocation = file.CurrentFileLocation;
                file.CurrentFileLocation = quarantinePath;
                file.FindingType = FindingType.Quarantined.ToString();
                file.QuarantineDateUtc = DateTime.UtcNow;
                file.LastUpdateDateUtc = DateTime.UtcNow;
                file.ErrorCategory = ErrorCategory.None;
                file.ErrorDetail = null;

                _repository.Update(file);

                _logger.LogInformation("Quarantined successfully: {File}", file.FindingFileName);
            }
            catch (Exception ex)
            {
                file.ErrorCategory = ErrorCategory.RetryExhausted;
                file.ErrorDetail = ex.Message;
                file.LastUpdateDateUtc = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogError(ex, "Error processing file: {File}", file.FindingFileName);
            }
        }

        _logger.LogInformation("=== Quarantine Job Completed ===");

        await Task.CompletedTask;
    }
}