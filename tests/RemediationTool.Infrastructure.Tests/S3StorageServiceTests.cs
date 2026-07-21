using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RemediationTool.Infrastructure;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class S3StorageServiceTests
{
    [Fact]
    public void Constructor_MissingBucket_ThrowsConfigurationError()
    {
        var client = new Mock<IAmazonS3>();
        var configuration = new TemporaryDirectoryFixture()
            .Configuration();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new S3StorageService(
                client.Object,
                configuration,
                NullLogger<S3StorageService>.Instance));

        Assert.Contains("AWS:BucketName", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, "AES256", null)]
    [InlineData(true, "aws:kms", "kms-key-1")]
    public async Task UploadAsync_AppliesConfiguredEncryptionAndNormalizesKey(
        bool encryptionEnabled,
        string? method,
        string? kmsKeyId)
    {
        var client = new Mock<IAmazonS3>();
        PutObjectRequest? captured = null;
        client
            .Setup(s3 => s3.PutObjectAsync(
                It.IsAny<PutObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());
        var service = CreateService(client, encryptionEnabled, method, kmsKeyId);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("payload"));

        await service.UploadAsync("/folder/file.txt", stream);

        Assert.NotNull(captured);
        Assert.Equal("test-bucket", captured.BucketName);
        Assert.Equal("folder/file.txt", captured.Key);
        Assert.Same(stream, captured.InputStream);

        if (!encryptionEnabled)
        {
            Assert.Null(captured.ServerSideEncryptionMethod);
        }
        else if (string.Equals(method, "aws:kms", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(ServerSideEncryptionMethod.AWSKMS, captured.ServerSideEncryptionMethod);
            Assert.Equal(kmsKeyId, captured.ServerSideEncryptionKeyManagementServiceKeyId);
        }
        else
        {
            Assert.Equal(ServerSideEncryptionMethod.AES256, captured.ServerSideEncryptionMethod);
        }
    }

    [Fact]
    public async Task DownloadAsync_BuffersResponseAndReturnsSeekableStreamAtPositionZero()
    {
        var payload = Encoding.UTF8.GetBytes("downloaded-content");
        var client = new Mock<IAmazonS3>();
        GetObjectRequest? captured = null;
        client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<GetObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = payload.Length,
                ResponseStream = new MemoryStream(payload)
            });
        var service = CreateService(client);

        await using var result = await service.DownloadAsync("/reports/file.csv");
        using var reader = new StreamReader(result, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync();

        Assert.Equal("test-bucket", captured?.BucketName);
        Assert.Equal("reports/file.csv", captured?.Key);
        Assert.True(result.CanSeek);
        Assert.Equal("downloaded-content", content);
        result.Position = 0;
        Assert.Equal('d', result.ReadByte());
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsStreamingOwnedStreamAndDisposesInnerStream()
    {
        var inner = new TrackingMemoryStream(Encoding.UTF8.GetBytes("streamed"));
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = inner.Length,
                ResponseStream = inner
            });
        var service = CreateService(client);

        await using (var result = await service.OpenReadAsync("stream/file.txt"))
        {
            Assert.False(ReferenceEquals(inner, result));
            using var reader = new StreamReader(result, Encoding.UTF8, leaveOpen: true);
            Assert.Equal("streamed", await reader.ReadToEndAsync());
        }

        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task OpenSeekableReadAsync_CopiesStreamingResponseToTemporarySeekableFile()
    {
        var payload = Encoding.UTF8.GetBytes("seekable-content");
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = payload.Length,
                ResponseStream = new MemoryStream(payload)
            });
        var service = CreateService(client);

        await using var result = await service.OpenSeekableReadAsync("file.xlsx");

        Assert.True(result.CanSeek);
        Assert.Equal(0, result.Position);
        result.Position = 9;
        Assert.Equal('c', result.ReadByte());
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForMetadataAndFalseForNotFound()
    {
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key == "exists.txt"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());
        client
            .Setup(s3 => s3.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key == "missing.txt"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        var service = CreateService(client);

        Assert.True(await service.ExistsAsync("exists.txt"));
        Assert.False(await service.ExistsAsync("missing.txt"));
        Assert.False(await service.ExistsAsync(" "));
    }

    [Fact]
    public async Task MoveAsync_CopiesWithEncryptionThenDeletesSource()
    {
        var client = new Mock<IAmazonS3>();
        var sequence = new MockSequence();
        CopyObjectRequest? copy = null;
        DeleteObjectRequest? delete = null;
        client
            .InSequence(sequence)
            .Setup(s3 => s3.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => copy = request)
            .ReturnsAsync(new CopyObjectResponse());
        client
            .InSequence(sequence)
            .Setup(s3 => s3.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => delete = request)
            .ReturnsAsync(new DeleteObjectResponse());
        var service = CreateService(
            client,
            encryptionEnabled: true,
            method: "KMS",
            kmsKeyId: "kms-key");

        await service.MoveAsync("/source/file.txt", "/destination/file.txt");

        Assert.NotNull(copy);
        Assert.Equal("test-bucket", copy.SourceBucket);
        Assert.Equal("source/file.txt", copy.SourceKey);
        Assert.Equal("test-bucket", copy.DestinationBucket);
        Assert.Equal("destination/file.txt", copy.DestinationKey);
        Assert.Equal(ServerSideEncryptionMethod.AWSKMS, copy.ServerSideEncryptionMethod);
        Assert.Equal("kms-key", copy.ServerSideEncryptionKeyManagementServiceKeyId);
        Assert.Equal("source/file.txt", delete?.Key);
    }

    [Fact]
    public async Task DeleteAsync_SendsNormalizedDeleteRequest()
    {
        var client = new Mock<IAmazonS3>();
        DeleteObjectRequest? captured = null;
        client
            .Setup(s3 => s3.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new DeleteObjectResponse());
        var service = CreateService(client);

        await service.DeleteAsync("/folder/file.txt");

        Assert.Equal("test-bucket", captured?.BucketName);
        Assert.Equal("folder/file.txt", captured?.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequiredKeyOperations_RejectBlankKeys(string? key)
    {
        var client = new Mock<IAmazonS3>();
        var service = CreateService(client);
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(key!, stream));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadAsync(key!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenReadAsync(key!));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(key!));
    }

    [Fact]
    public async Task DownloadAndOpenRead_NotFound_PropagateAmazonException()
    {
        var client = new Mock<IAmazonS3>();
        client
            .Setup(s3 => s3.GetObjectAsync(
                It.IsAny<GetObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        var service = CreateService(client);

        var buffered = await Assert.ThrowsAsync<AmazonS3Exception>(
            () => service.DownloadAsync("missing.txt"));
        var streamed = await Assert.ThrowsAsync<AmazonS3Exception>(
            () => service.OpenReadAsync("missing.txt"));

        Assert.Equal(HttpStatusCode.NotFound, buffered.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, streamed.StatusCode);
    }

    [Fact]
    public async Task PreCancelledOperations_DoNotCallAws()
    {
        var client = new Mock<IAmazonS3>();
        var service = CreateService(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var stream = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.UploadAsync("file.txt", stream, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.OpenReadAsync("file.txt", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.MoveAsync("source", "destination", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DeleteAsync("file.txt", cancellation.Token));

        client.VerifyNoOtherCalls();
    }

    private static S3StorageService CreateService(
        Mock<IAmazonS3> client,
        bool encryptionEnabled = false,
        string? method = null,
        string? kmsKeyId = null)
    {
        var configuration = new TemporaryDirectoryFixture().Configuration(
            ("AWS:BucketName", "test-bucket"),
            ("AWS:UseServerSideEncryption", encryptionEnabled.ToString()),
            ("AWS:ServerSideEncryptionMethod", method),
            ("AWS:KmsKeyId", kmsKeyId));

        return new S3StorageService(
            client.Object,
            configuration,
            NullLogger<S3StorageService>.Instance);
    }

    private static AmazonS3Exception NotFound()
        => new("not found")
        {
            StatusCode = HttpStatusCode.NotFound
        };

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public TrackingMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Disposed = true;

            base.Dispose(disposing);
        }
    }
}
