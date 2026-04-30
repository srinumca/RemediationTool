
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.Infrastructure;

public class LocalStorageService : IStorageService
{
    private readonly string _basePath = "storage";
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(ILogger<LocalStorageService> logger)
    {
        _logger = logger;
    }

    public async Task UploadAsync(string key, Stream data)
    {
        try
        {
            var path = Path.Combine(_basePath, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var fs = new FileStream(path, FileMode.Create);
            await data.CopyToAsync(fs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local upload failed for key {Key}", key);
            throw;
        }
    }

    public Task<Stream> DownloadAsync(string key)
    {
        try
        {
            var path = Path.Combine(_basePath, key);
            return Task.FromResult<Stream>(File.OpenRead(path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local download failed for key {Key}", key);
            throw;
        }
    }

    public Task MoveAsync(string sourceKey, string destinationKey)
    {
        try
        {
            var src = Path.Combine(_basePath, sourceKey);
            var dest = Path.Combine(_basePath, destinationKey);

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(src, dest, true);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local move failed from {Source} to {Dest}", sourceKey, destinationKey);
            throw;
        }
    }

    public Task DeleteAsync(string key)
    {
        try
        {
            var path = Path.Combine(_basePath, key);
            if (File.Exists(path))
                File.Delete(path);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local delete failed for key {Key}", key);
            throw;
        }
    }
}
