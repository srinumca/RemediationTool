using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace RemediationTool.Application.Services;

/// <summary>
/// POC restore service — restores quarantined files to their original location.
/// NOTE: This is placeholder POC logic. When the File Restoration requirement
/// (Req 41-58) is properly implemented, this service will be fully rewritten
/// with the confirmation dialog, ticket identifier capture, priority processing,
/// metadata preservation, and append-only data model updates per spec.
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

    public async Task RestoreAsync(Guid fileId)
    {
        var file = _repository.GetById(fileId);

        if (file == null)
        {
            _logger.LogError("File not found in metadata: {FileId}", fileId);
            return;
        }

        if (file.FindingType != FindingType.Quarantined)
        {
            _logger.LogWarning("File is not in Quarantined state: {File}", file.FindingFileName);
            return;
        }

        try
        {
            var quarantinePath = file.CurrentFileLocation;
            var originalPath = file.OriginalFileLocation;

            if (string.IsNullOrWhiteSpace(quarantinePath) || !File.Exists(quarantinePath))
            {
                file.ErrorCategory = ErrorCategory.RestorationQuarantineFileMissing;
                file.ErrorDetail = $"Quarantine file not found: {quarantinePath}";
                file.LastUpdateDateUtc = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogError("Quarantine file missing: {Path}", quarantinePath);
                return;
            }

            if (string.IsNullOrWhiteSpace(originalPath))
            {
                file.ErrorCategory = ErrorCategory.RestorationTargetPathMissing;
                file.ErrorDetail = "Original file location is not recorded for this finding.";
                file.LastUpdateDateUtc = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogError("Original file location missing for: {File}", file.FindingFileName);
                return;
            }

            // Restore file to original location
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            File.Copy(quarantinePath, originalPath, overwrite: true);
            File.Delete(quarantinePath);

            // Remove breadcrumb stub
            var stubPath = originalPath + "_Retention_Placeholder";
            if (File.Exists(stubPath))
                File.Delete(stubPath);

            // Update data model
            file.FindingType = FindingType.Restored;
            file.RestorationDateUtc = DateTime.UtcNow;
            file.CurrentFileLocation = originalPath;
            file.OriginalFileLocation = null;
            file.QuarantineDateUtc = null;
            file.LastUpdateDateUtc = DateTime.UtcNow;
            file.ErrorCategory = ErrorCategory.None;
            file.ErrorDetail = null;

            _repository.Update(file);

            _logger.LogInformation("File restored: {File}", file.FindingFileName);
        }
        catch (Exception ex)
        {
            file.ErrorCategory = ErrorCategory.RetryExhausted;
            file.ErrorDetail = ex.Message;
            file.LastUpdateDateUtc = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogError(ex, "Restore failed: {File}", file.FindingFileName);
        }
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetLatestByFindingType(FindingType.Quarantined).ToList();

        foreach (var file in files)
            await RestoreAsync(file.Id);
    }
}