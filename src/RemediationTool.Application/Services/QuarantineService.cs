using RemediationTool.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace RemediationTool.Application.Services;

public class QuarantineService
{
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly ILogger<QuarantineService> _logger;

    private const int RetentionYears = 10;   // 🔥 Important
    private const int RetryCount = 3;

    public QuarantineService(
        IFileFindingRepository repository,
        IStorageService storage,
        ILogger<QuarantineService> logger)
    {
        _repository = repository;
        _storage = storage;
        _logger = logger;
    }

    public async Task ProcessAsync()
    {
        var files = _repository.GetAll()
            .Where(x => x.Status == "Loaded")
            .ToList();

        foreach (var file in files)
        {
            try
            {
                // 🔥 Rule check
                if (file.LastModifiedDate > DateTime.UtcNow.AddYears(-RetentionYears))
                {
                    _logger.LogInformation("Skipping {File} (retention not met)", file.FileName);
                    continue;
                }

                var sourceKey = $"input/{file.FileName}";
                var destKey = $"quarantine/{file.FileName}";

                await ExecuteWithRetryAsync(async () =>
                {
                    // 🔥 CORE LINE (VERY IMPORTANT)
                    try
                    {
                        await _storage.MoveAsync(sourceKey, destKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Storage move failed for {File}", file.FileName);
                        throw;
                    }

                    file.Status = "Quarantined";
                    file.QuarantinePath = destKey;

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

                _logger.LogInformation("Quarantined {File}", file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing {File}", file.FileName);
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

                if (attempts >= RetryCount)
                    throw;

                await Task.Delay(500);
            }
        }
    }
}