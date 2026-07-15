using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.Infrastructure;

public class LocalStorageService : IStorageService, IStreamingStorageService
{
    private const int FileBufferSize = 81920;
    private readonly string _rootPath;

    public LocalStorageService(IConfiguration configuration)
    {
        _rootPath = Path.GetFullPath(configuration["Storage:LocalRootPath"] ?? "storage");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task UploadAsync(
        string key,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = GetFullPath(key);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await stream.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<Stream> DownloadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var fullPath = GetFullPath(key);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found in local storage: {key}", fullPath);

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    public Task<Stream> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default)
        => DownloadAsync(key, cancellationToken);

    public Task<Stream> OpenSeekableReadAsync(
        string key,
        CancellationToken cancellationToken = default)
        => DownloadAsync(key, cancellationToken);

    public Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(false);

        return Task.FromResult(File.Exists(GetFullPath(key)));
    }

    public Task MoveAsync(
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("Source key is required.", nameof(sourceKey));

        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("Destination key is required.", nameof(destinationKey));

        var sourcePath = GetFullPath(sourceKey);
        var destinationPath = GetFullPath(destinationKey);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourceKey}", sourcePath);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        File.Move(sourcePath, destinationPath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(key));

        var fullPath = GetFullPath(key);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private string GetFullPath(string key)
    {
        var normalizedKey = key
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(_rootPath, normalizedKey));
    }
}
