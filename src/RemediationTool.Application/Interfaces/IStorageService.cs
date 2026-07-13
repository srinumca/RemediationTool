namespace RemediationTool.Application.Interfaces;

public interface IStorageService
{
    Task UploadAsync(string key, Stream data);

    Task<Stream> DownloadAsync(string key);

    Task<bool> ExistsAsync(string key);

    Task MoveAsync(string sourceKey, string destinationKey);

    Task DeleteAsync(string key);
}
