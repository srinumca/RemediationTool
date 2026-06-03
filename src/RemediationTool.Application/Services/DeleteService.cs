using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace RemediationTool.Application.Services;

/// <summary>
/// POC delete service — permanently deletes quarantined files and their stubs.
/// NOTE: This is placeholder POC logic. When the Automated Deletion requirement
/// (Req 59-66) is properly implemented, this service will be fully rewritten
/// to evaluate quarantine hold periods, hard-delete files and breadcrumbs,
/// and update the data model using the append-only pattern.
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
        var file = _repository.GetById(id);

        if (file == null)
        {
            _logger.LogWarning("File not found for delete: {Id}", id);
            return;
        }

        if (file.FindingType != FindingType.Quarantined.ToString())
        {
            _logger.LogWarning("File is not in Quarantined state: {File}", file.FindingFileName);
            return;
        }

        try
        {
            var quarantinePath = file.CurrentFileLocation.ToString();
            var stubPath = (file.OriginalFileLocation ?? string.Empty) + "_Retention_Placeholder";

            // Delete quarantined file
            if (!string.IsNullOrWhiteSpace(quarantinePath) && File.Exists(quarantinePath))
                File.Delete(quarantinePath);

            // Delete breadcrumb stub
            if (File.Exists(stubPath))
                File.Delete(stubPath);

            // Update data model
            file.FindingType = FindingType.Deleted.ToString();
            file.DeletionDateUtc = DateTime.UtcNow;
            file.CurrentFileLocation = string.Empty;
            file.LastUpdateDateUtc = DateTime.UtcNow;
            file.ErrorCategory = ErrorCategory.None;
            file.ErrorDetail = null;

            _repository.Update(file);

            _logger.LogInformation("File deleted permanently: {File}", file.FindingFileName);
        }
        catch (Exception ex)
        {
            file.ErrorCategory = ErrorCategory.RetryExhausted;
            file.ErrorDetail = ex.Message.ToString();
            file.LastUpdateDateUtc = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogError(ex, "Delete failed: {File}", file.FindingFileName);
        }

        await Task.CompletedTask;
    }

    public async Task DeleteAllAsync()
    {
        var files = _repository.GetLatestByFindingType(FindingType.Quarantined).ToList();

        foreach (var file in files)
            await DeleteAsync(file.Id);
    }
}