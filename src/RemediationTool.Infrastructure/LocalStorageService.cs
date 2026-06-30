using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.Infrastructure;

public class LocalStorageService : IStorageService
{
    private readonly string _rootPath;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(IConfiguration configuration, ILogger<LocalStorageService> logger)
    {
        _logger = logger;
        _rootPath = configuration["Storage:LocalRootPath"] ?? "storage";
        _logger.LogInformation("LocalStorageService initialized with RootPath: {RootPath}", _rootPath);
    }

    /// <summary>
    /// Uploads a file to local storage.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="stream"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task UploadAsync(string key, Stream stream)
    {
        _logger.LogDebug("Uploading file to local storage. Key: {Key}, StreamLength: {StreamLength}", key, stream?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Upload rejected: Storage key is required");
            throw new ArgumentException("Storage key is required.", nameof(key));
        }

        try
        {
            var fullPath = GetFullPath(key);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogDebug("Directory created for upload. Directory: {Directory}", directory);
            }

            await using var fileStream = File.Create(fullPath);
            await stream.CopyToAsync(fileStream);
            _logger.LogInformation("File uploaded successfully to local storage. Key: {Key}, Path: {Path}, Size: {Size} bytes", 
                key, fullPath, fileStream.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to local storage. Key: {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Downloads a file from local storage.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public async Task<Stream> DownloadAsync(string key)
    {
        _logger.LogDebug("Downloading file from local storage. Key: {Key}", key);

        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Download rejected: Storage key is required");
            throw new ArgumentException("Storage key is required.", nameof(key));
        }

        try
        {
            var fullPath = GetFullPath(key);

            if (!File.Exists(fullPath))
            {
                _logger.LogError("File not found in local storage. Key: {Key}, Path: {Path}", key, fullPath);
                throw new FileNotFoundException($"File not found in local storage: {key}");
            }

            var memoryStream = new MemoryStream();

            await using var fileStream = File.OpenRead(fullPath);
            await fileStream.CopyToAsync(memoryStream);

            memoryStream.Position = 0;
            _logger.LogInformation("File downloaded successfully from local storage. Key: {Key}, Size: {Size} bytes", key, memoryStream.Length);
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from local storage. Key: {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Moves a file within local storage from sourceKey to destinationKey.
    /// </summary>
    /// <param name="sourceKey"></param>
    /// <param name="destinationKey"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public async Task MoveAsync(string sourceKey, string destinationKey)
    {
        _logger.LogDebug("Moving file in local storage. SourceKey: {SourceKey}, DestinationKey: {DestinationKey}", sourceKey, destinationKey);

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            _logger.LogWarning("Move rejected: Source key is required");
            throw new ArgumentException("Source key is required.", nameof(sourceKey));
        }

        if (string.IsNullOrWhiteSpace(destinationKey))
        {
            _logger.LogWarning("Move rejected: Destination key is required");
            throw new ArgumentException("Destination key is required.", nameof(destinationKey));
        }

        try
        {
            var sourcePath = GetFullPath(sourceKey);
            var destinationPath = GetFullPath(destinationKey);

            if (!File.Exists(sourcePath))
            {
                _logger.LogError("Source file not found for move. SourceKey: {SourceKey}, Path: {Path}", sourceKey, sourcePath);
                throw new FileNotFoundException($"Source file not found: {sourceKey}");
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await using var sourceStream = File.OpenRead(sourcePath);
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream);

            File.Delete(sourcePath);
            _logger.LogInformation("File moved successfully in local storage. SourceKey: {SourceKey}, DestinationKey: {DestinationKey}", 
                sourceKey, destinationKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving file in local storage. SourceKey: {SourceKey}, DestinationKey: {DestinationKey}", 
                sourceKey, destinationKey);
            throw;
        }
    }

    /// <summary>
    /// Deletes a file from local storage.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public Task DeleteAsync(string key)
    {
        _logger.LogDebug("Deleting file from local storage. Key: {Key}", key);

        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Delete rejected: Storage key is required");
            throw new ArgumentException("Storage key is required.", nameof(key));
        }

        try
        {
            var fullPath = GetFullPath(key);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("File deleted successfully from local storage. Key: {Key}", key);
            }
            else
            {
                _logger.LogDebug("File not found for deletion. Key: {Key}, Path: {Path}", key, fullPath);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from local storage. Key: {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Gets the full path for a given storage key, normalizing directory separators.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private string GetFullPath(string key)
    {
        var normalizedKey = key
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.Combine(_rootPath, normalizedKey);
    }
}