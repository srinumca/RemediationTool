using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Application.Services;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class Phase2AsyncIoTests
{
    [Fact]
    public async Task OpenSourceReadAsync_UsesForwardOnlyStreamingForCsv()
    {
        var storage = new TrackingStorage();
        var options = CreateStreamingOptions();

        await using var stream = await IngestionAsyncIo.OpenSourceReadAsync(
            storage,
            options,
            "input/report.csv",
            ".csv",
            CancellationToken.None);

        Assert.Equal(1, storage.StreamingReadCount);
        Assert.Equal(0, storage.SeekableReadCount);
        Assert.Equal(0, storage.BufferedReadCount);
    }

    [Fact]
    public async Task OpenSourceReadAsync_UsesSeekableStorageForXlsx()
    {
        var storage = new TrackingStorage();
        var options = CreateStreamingOptions();

        await using var stream = await IngestionAsyncIo.OpenSourceReadAsync(
            storage,
            options,
            "input/report.xlsx",
            ".xlsx",
            CancellationToken.None);

        Assert.Equal(0, storage.StreamingReadCount);
        Assert.Equal(1, storage.SeekableReadCount);
        Assert.Equal(0, storage.BufferedReadCount);
        Assert.True(stream.CanSeek);
    }

    [Fact]
    public async Task OpenSourceReadAsync_UsesBufferedCompatibilityPathWhenStreamingDisabled()
    {
        var storage = new TrackingStorage();
        var options = new IngestionProcessingOptions
        {
            EnableHighVolumeStreaming = false,
            LegacyFallbackEnabled = true
        };

        await using var stream = await IngestionAsyncIo.OpenSourceReadAsync(
            storage,
            options,
            "input/report.csv",
            ".csv",
            CancellationToken.None);

        Assert.Equal(0, storage.StreamingReadCount);
        Assert.Equal(0, storage.SeekableReadCount);
        Assert.Equal(1, storage.BufferedReadCount);
    }

    [Fact]
    public async Task OpenSourceReadAsync_ThrowsWhenStreamingRequiredButUnavailable()
    {
        var storage = new BufferedOnlyStorage();
        var options = new IngestionProcessingOptions
        {
            EnableHighVolumeStreaming = true,
            LegacyFallbackEnabled = false
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IngestionAsyncIo.OpenSourceReadAsync(
                storage,
                options,
                "input/report.csv",
                ".csv",
                CancellationToken.None));

        Assert.Contains("legacy fallback is disabled", exception.Message);
        Assert.Equal(0, storage.BufferedReadCount);
    }

    [Fact]
    public async Task OpenSourceReadAsync_PropagatesCancellationBeforeIoStarts()
    {
        var storage = new TrackingStorage();
        var options = CreateStreamingOptions();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            IngestionAsyncIo.OpenSourceReadAsync(
                storage,
                options,
                "input/report.csv",
                ".csv",
                cancellation.Token));

        Assert.Equal(0, storage.StreamingReadCount);
        Assert.Equal(0, storage.SeekableReadCount);
        Assert.Equal(0, storage.BufferedReadCount);
    }

    private static IngestionProcessingOptions CreateStreamingOptions()
        => new()
        {
            EnableHighVolumeStreaming = true,
            LegacyFallbackEnabled = true
        };

    private sealed class TrackingStorage : IStorageService, IStreamingStorageService
    {
        public int StreamingReadCount { get; private set; }
        public int SeekableReadCount { get; private set; }
        public int BufferedReadCount { get; private set; }

        public Task UploadAsync(
            string key,
            Stream data,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Stream> DownloadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BufferedReadCount++;
            return Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }));
        }

        public Task<Stream> OpenReadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StreamingReadCount++;
            return Task.FromResult<Stream>(new NonSeekableReadStream(new byte[] { 1, 2, 3 }));
        }

        public Task<Stream> OpenSeekableReadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SeekableReadCount++;
            return Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }));
        }

        public Task<bool> ExistsAsync(
            string key,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task MoveAsync(
            string sourceKey,
            string destinationKey,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class BufferedOnlyStorage : IStorageService
    {
        public int BufferedReadCount { get; private set; }

        public Task UploadAsync(
            string key,
            Stream data,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Stream> DownloadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            BufferedReadCount++;
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<bool> ExistsAsync(
            string key,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task MoveAsync(
            string sourceKey,
            string destinationKey,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] buffer)
            : base(buffer)
        {
        }

        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin loc)
            => throw new NotSupportedException();

        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
    }
}
