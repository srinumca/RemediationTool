namespace RemediationTool.Application.Interfaces;

public interface IStorageService
{
    Task UploadAsync(
        string key,
        Stream data,
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task MoveAsync(
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}
