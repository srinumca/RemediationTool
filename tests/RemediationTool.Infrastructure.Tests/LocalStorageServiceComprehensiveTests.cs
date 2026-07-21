using System.Text;
using RemediationTool.Infrastructure;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class LocalStorageServiceComprehensiveTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _fixture;

    public LocalStorageServiceComprehensiveTests(TemporaryDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UploadAndDownload_NestedKey_RoundTripsBytes()
    {
        var service = CreateService();
        var expected = Encoding.UTF8.GetBytes("payload-123");
        await using var input = new MemoryStream(expected);

        await service.UploadAsync("jobs/2026/report.csv", input);
        await using var downloaded = await service.DownloadAsync("jobs/2026/report.csv");
        using var reader = new MemoryStream();
        await downloaded.CopyToAsync(reader);

        Assert.Equal(expected, reader.ToArray());
        Assert.True(await service.ExistsAsync("jobs/2026/report.csv"));
    }

    [Fact]
    public async Task Upload_NormalizesBackslashesAndOverwritesExistingFile()
    {
        var service = CreateService();
        await using var first = new MemoryStream(Encoding.UTF8.GetBytes("first"));
        await using var second = new MemoryStream(Encoding.UTF8.GetBytes("second"));

        await service.UploadAsync("folder\\item.txt", first);
        await service.UploadAsync("folder/item.txt", second);
        await using var downloaded = await service.DownloadAsync("folder/item.txt");
        using var reader = new StreamReader(downloaded);

        Assert.Equal("second", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAndSeekableRead_ReturnReadableSeekableStreams()
    {
        var service = CreateService();
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("abcdef"));
        await service.UploadAsync("stream/file.txt", input);

        await using var streaming = await service.OpenReadAsync("stream/file.txt");
        await using var seekable = await service.OpenSeekableReadAsync("stream/file.txt");

        Assert.True(streaming.CanRead);
        Assert.True(streaming.CanSeek);
        Assert.True(seekable.CanRead);
        Assert.True(seekable.CanSeek);
        seekable.Position = 3;
        Assert.Equal('d', seekable.ReadByte());
    }

    [Fact]
    public async Task Move_CreatesDestinationDirectoryAndOverwritesDestination()
    {
        var service = CreateService();
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("source"));
        await using var destination = new MemoryStream(Encoding.UTF8.GetBytes("old"));
        await service.UploadAsync("source/file.txt", source);
        await service.UploadAsync("destination/file.txt", destination);

        await service.MoveAsync("source/file.txt", "destination/file.txt");

        Assert.False(await service.ExistsAsync("source/file.txt"));
        Assert.True(await service.ExistsAsync("destination/file.txt"));
        await using var moved = await service.DownloadAsync("destination/file.txt");
        using var reader = new StreamReader(moved);
        Assert.Equal("source", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Delete_IsIdempotent()
    {
        var service = CreateService();
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("content"));
        await service.UploadAsync("delete/file.txt", input);

        await service.DeleteAsync("delete/file.txt");
        await service.DeleteAsync("delete/file.txt");

        Assert.False(await service.ExistsAsync("delete/file.txt"));
    }

    [Fact]
    public async Task Download_MissingFile_ThrowsFileNotFoundExceptionWithResolvedPath()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.DownloadAsync("missing/file.txt"));

        Assert.Contains("missing/file.txt", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.FileName);
    }

    [Fact]
    public async Task Move_MissingSource_ThrowsAndDoesNotCreateDestination()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.MoveAsync("missing.txt", "new/location.txt"));

        Assert.False(await service.ExistsAsync("new/location.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidKeys_AreRejected(string? key)
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UploadAsync(key!, stream));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.DownloadAsync(key!));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.DeleteAsync(key!));
        Assert.False(await service.ExistsAsync(key!));
    }

    [Theory]
    [InlineData(null, "destination")]
    [InlineData("source", null)]
    [InlineData("", "destination")]
    [InlineData("source", " ")]
    public async Task Move_InvalidKeys_AreRejected(string? source, string? destination)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.MoveAsync(source!, destination!));
    }

    [Fact]
    public async Task Operations_HonorPreCancelledToken()
    {
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.UploadAsync("file.txt", stream, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync("file.txt", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExistsAsync("file.txt", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.MoveAsync("a", "b", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DeleteAsync("file.txt", cancellation.Token));
    }

    private LocalStorageService CreateService()
        => new(_fixture.Configuration(("Storage:LocalRootPath", _fixture.RootPath)));
}
