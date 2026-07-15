namespace RemediationTool.Application.Interfaces;

/// <summary>
/// Optional storage contract for high-volume reads.
/// CSV callers can consume a forward-only stream, while XLSX and Parquet callers
/// can request a temporary seekable stream without buffering the full object in memory.
/// </summary>
public interface IStreamingStorageService
{
    Task<Stream> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenSeekableReadAsync(
        string key,
        CancellationToken cancellationToken = default);
}
