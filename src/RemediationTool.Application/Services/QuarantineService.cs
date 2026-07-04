using Microsoft.Extensions.Logging;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Quarantine service — moves eligible files to quarantine.
/// Uses FileStatus enum (not FindingType) for workflow state.
/// </summary>
public class QuarantineService
{
    private readonly IFileFindingRepository _repository;
    private readonly ILogger<QuarantineService> _logger;
    private readonly IAuditLogger _auditLogger;

    private const int RetentionYears = 10;

    private readonly string _basePath;
    private readonly string _sourceFolder;
    private readonly string _quarantineFolder;

    public QuarantineService(
        IFileFindingRepository repository,
        ILogger<QuarantineService> logger,
        IAuditLogger auditLogger)
    {
        _repository = repository;
        _logger = logger;
        _auditLogger = auditLogger;

        _basePath = Path.Combine(Directory.GetCurrentDirectory(), "storage");
        _sourceFolder = Path.Combine(_basePath, "source");
        _quarantineFolder = Path.Combine(_basePath, "quarantine");

        Directory.CreateDirectory(_sourceFolder);
        Directory.CreateDirectory(_quarantineFolder);
    }

    public async Task ProcessAsync()
    {
        _logger.LogInformation("[QUARANTINE RUN START] Scanning for files with Status=PendingQuarantine...");

        var files = _repository.GetAll()
            .Where(x => x.Status == FileStatus.PendingQuarantine)
            .ToList();

        _logger.LogInformation("[QUARANTINE RUN] Found {Count} file(s) pending quarantine.", files.Count);

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            try
            {
                _logger.LogInformation(
                    "[QUARANTINE FILE] Id: {Id} FileName: {File} — starting quarantine.",
                    file.Id, file.FileName);

                // Retention check
                if (file.LastModifiedDate > DateTime.UtcNow.AddYears(-RetentionYears))
                {
                    file.Status = FileStatus.NotYetStarted;
                    file.UpdatedDate = DateTime.UtcNow;
                    _repository.Update(file);
                    _logger.LogInformation(
                        "[QUARANTINE SKIP] Id: {Id} FileName: {File} — last modified {LastModified:yyyy-MM-dd}, retention threshold not reached.",
                        file.Id, file.FileName, file.LastModifiedDate);
                    skipped++;
                    continue;
                }

                var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), file.FilePath);

                if (!File.Exists(sourcePath))
                {
                    file.Status = FileStatus.Error;
                    file.ErrorReason = $"File not found at source: {sourcePath}";
                    file.ErrorCategory = ErrorCategoryResolver.SourceFileMissing().ToString();
                    file.UpdatedDate = DateTime.UtcNow;
                    _repository.Update(file);
                    _logger.LogError(
                        "[QUARANTINE FAILED] Id: {Id} FileName: {File} — source file not found at {Path}.",
                        file.Id, file.FileName, sourcePath);
                    failed++;
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

                _logger.LogInformation(
                    "[QUARANTINE MOVE] Id: {Id} FileName: {File} — moved to quarantine path: {QuarantinePath}",
                    file.Id, file.FileName, quarantinePath);

                // Create stub
                var stubPath = sourcePath + "_Retention_Placeholder";
                File.WriteAllText(stubPath,
                    "This file was moved due to ADP retention policy. Contact admin to request restore.");

                _logger.LogInformation(
                    "[QUARANTINE STUB] Id: {Id} FileName: {File} — retention placeholder created at: {StubPath}",
                    file.Id, file.FileName, stubPath);

                // Update to complete
                file.Status = FileStatus.QuarantineComplete;
                file.ErrorCategory = ErrorCategory.None.ToString();
                file.QuarantinePath = quarantinePath;
                file.QuarantineDate = DateTime.UtcNow;
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogInformation(
                    "[QUARANTINE COMPLETE] Id: {Id} FileName: {File} — status updated to QuarantineComplete.",
                    file.Id, file.FileName);

                // Audit event — compliance-relevant, routed to the separate
                // audit log file with long retention.
                _auditLogger.RecordEvent(
                    eventType: "FileQuarantined",
                    entityId: file.Id.ToString(),
                    actor: "System",
                    outcome: "Success",
                    details: new { file.FileName, OriginalPath = sourcePath, QuarantinePath = quarantinePath });

                succeeded++;
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
                    "[QUARANTINE FAILED] Id: {Id} FileName: {File} — quarantine failed. Error: {Error}",
                    file.Id, file.FileName, ex.Message);

                _auditLogger.RecordEvent(
                    eventType: "FileQuarantined",
                    entityId: file.Id.ToString(),
                    actor: "System",
                    outcome: "Failed",
                    details: new { file.FileName, Error = ex.Message });

                failed++;
            }
        }

        _logger.LogInformation(
            "[QUARANTINE RUN COMPLETE] Processed: {Processed} Succeeded: {Succeeded} Failed: {Failed} Skipped: {Skipped}",
            files.Count, succeeded, failed, skipped);

        await Task.CompletedTask;
    }
}