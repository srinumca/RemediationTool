using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.Infrastructure;

public class LocalStorageService : IStorageService
{
    private readonly string _rootPath;

    public LocalStorageService(IConfiguration configuration)
    {
        _rootPath = configuration["Storage:LocalRootPath"] ?? "storage";
    }

    public async Task UploadAsync(string key, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var fullPath = GetFullPath(key);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = File.Create(fullPath);
        await stream.CopyToAsync(fileStream);
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var fullPath = GetFullPath(key);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found in local storage: {key}");

        var memoryStream = new MemoryStream();

        await using var fileStream = File.OpenRead(fullPath);
        await fileStream.CopyToAsync(memoryStream);

        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task MoveAsync(string sourceKey, string destinationKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("Source key is required.", nameof(sourceKey));

        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("Destination key is required.", nameof(destinationKey));

        var sourcePath = GetFullPath(sourceKey);
        var destinationPath = GetFullPath(destinationKey);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourceKey}");

        var destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream);

        File.Delete(sourcePath);
    }

    public Task DeleteAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var fullPath = GetFullPath(key);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string GetFullPath(string key)
    {
        var normalizedKey = key
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.Combine(_rootPath, normalizedKey);
    }
}