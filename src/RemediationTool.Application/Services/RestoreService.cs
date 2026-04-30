using RemediationTool.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace RemediationTool.Application.Services;

public class RestoreService
{
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly ILogger<RestoreService> _logger;

    private const int RetryCount = 3;

    public RestoreService(
        IFileFindingRepository repository,
        IStorageService storage,
        ILogger<RestoreService> logger)
    {
        _repository = repository;
        _storage = storage;
        _logger = logger;
    }

    public async Task RestoreAsync(string fileName)
    {
        try
        {
            var file = _repository.GetAll()
                .FirstOrDefault(x => x.FileName == fileName);

            if (file == null)
            {
                _logger.LogWarning("File not found in repository: {File}", fileName);
                return;
            }

            if (!string.Equals(file.Status, "Quarantined", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("File not in quarantine: {File}", fileName);
                return;
            }

            var sourceKey = $"quarantine/{file.FileName}";
            var destKey = $"input/{file.FileName}";

            await ExecuteWithRetryAsync(async () =>
            {
                try
                {
                    await _storage.MoveAsync(sourceKey, destKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Storage move failed for {File}", file.FileName);
                    throw;
                }

                file.Status = "Restored";
                file.QuarantinePath = null;

                try
                {
                    _repository.Update(file);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Repository update failed for {File}", file.FileName);
                    throw;
                }
            });

            _logger.LogInformation("Restored {File}", file.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring file {File}", fileName);
            throw;
        }
    }

    public async Task RestoreAllAsync()
    {
        var files = _repository.GetAll()
            .Where(x => string.Equals(x.Status, "Quarantined", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in files)
        {
            try
            {
                await RestoreAsync(file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring {File}", file.FileName);
            }
        }
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action)
    {
        int attempts = 0;

        while (attempts < RetryCount)
        {
            try
            {
                await action();
                return;
            }
            catch
            {
                attempts++;
                if (attempts >= RetryCount) throw;
                await Task.Delay(500);
            }
        }
    }
}