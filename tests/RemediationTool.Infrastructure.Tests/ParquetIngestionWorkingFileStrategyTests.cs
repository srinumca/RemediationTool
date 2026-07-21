using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.Strategies;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class ParquetIngestionWorkingFileStrategyTests
{
    [Fact]
    public void Format_IsParquet()
    {
        var fixture = new StrategyFixture();

        Assert.Equal("Parquet", fixture.Strategy.Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteAsync_BlankJobId_Throws(string? jobId)
    {
        var fixture = new StrategyFixture();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Strategy.WriteAsync(
                jobId!,
                "report.csv",
                new[] { CreateFinding("one.txt") }));
    }

    [Fact]
    public async Task WriteAsync_NullFindings_Throws()
    {
        var fixture = new StrategyFixture();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Strategy.WriteAsync("job-1", "report.csv", null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_GeneratesNonEmptyParquetAndUploadsExpectedResult(
        bool highVolumeStreaming)
    {
        var fixture = new StrategyFixture(
            enableHighVolumeStreaming: highVolumeStreaming,
            validateAfterWrite: false,
            parquetRowGroupSize: 0);
        var findings = new[]
        {
            CreateFinding("one.txt"),
            CreateFinding("two.txt")
        };

        var result = await fixture.Strategy.WriteAsync(
            "job-1",
            "reports/source.csv",
            findings);

        Assert.Equal("Parquet", result.Format);
        Assert.Equal(2, result.RecordCount);
        Assert.StartsWith("ingestion-working/", result.Path, StringComparison.Ordinal);
        Assert.EndsWith("/job-1/source.parquet", result.Path, StringComparison.Ordinal);
        Assert.NotNull(fixture.UploadedBytes);
        Assert.True(fixture.UploadedBytes.Length > 8);
        Assert.Equal("PAR1", System.Text.Encoding.ASCII.GetString(fixture.UploadedBytes, 0, 4));
        Assert.Equal(
            "PAR1",
            System.Text.Encoding.ASCII.GetString(
                fixture.UploadedBytes,
                fixture.UploadedBytes.Length - 4,
                4));
        fixture.Storage.Verify(
            storage => storage.ExistsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteAsync_VerificationEnabled_RequiresStoredObject()
    {
        var fixture = new StrategyFixture(validateAfterWrite: true, existsAfterUpload: false);

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            fixture.Strategy.WriteAsync(
                "job-verify",
                "report.csv",
                new[] { CreateFinding("one.txt") }));

        Assert.Contains("verification failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.Verify(
            storage => storage.ExistsAsync(
                It.Is<string>(path => path.EndsWith("/job-verify/report.parquet", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteAsync_VerificationEnabled_ReturnsWhenObjectExists()
    {
        var fixture = new StrategyFixture(validateAfterWrite: true, existsAfterUpload: true);

        var result = await fixture.Strategy.WriteAsync(
            "job-verify",
            "report.csv",
            new[] { CreateFinding("one.txt") });

        Assert.Equal(1, result.RecordCount);
        fixture.Storage.Verify(
            storage => storage.ExistsAsync(result.Path, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteAsync_StorageFailure_IsPropagated()
    {
        var fixture = new StrategyFixture(uploadException: new IOException("storage failed"));

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            fixture.Strategy.WriteAsync(
                "job-failure",
                "report.csv",
                new[] { CreateFinding("one.txt") }));

        Assert.Equal("storage failed", exception.Message);
    }

    [Fact]
    public async Task WriteAsync_PreCancelledToken_DoesNotUpload()
    {
        var fixture = new StrategyFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Strategy.WriteAsync(
                "job-cancel",
                "report.csv",
                new[] { CreateFinding("one.txt") },
                cancellation.Token));

        fixture.Storage.Verify(
            storage => storage.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static FileFinding CreateFinding(string fileName)
        => new()
        {
            IngestionJobId = "job-1",
            InboundFileName = "report.csv",
            FindingFileName = fileName,
            FindingFileFormat = "txt",
            FindingFileSizeBytes = 100,
            CurrentFileLocation = $"/source/{fileName}",
            FindingType = "Obsolete",
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG",
            LastModifiedDateUtc = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc)
        };

    private sealed class StrategyFixture
    {
        public StrategyFixture(
            bool enableHighVolumeStreaming = false,
            bool validateAfterWrite = false,
            bool existsAfterUpload = true,
            int parquetRowGroupSize = 50_000,
            Exception? uploadException = null)
        {
            Storage = new Mock<IStorageService>();
            Storage
                .Setup(storage => storage.UploadAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, Stream stream, CancellationToken _) =>
                {
                    if (uploadException is not null)
                        throw uploadException;

                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    UploadedBytes = copy.ToArray();
                    return Task.CompletedTask;
                });
            Storage
                .Setup(storage => storage.ExistsAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(existsAfterUpload);

            Strategy = new ParquetIngestionWorkingFileStrategy(
                Storage.Object,
                Options.Create(new IngestionProcessingOptions
                {
                    EnableHighVolumeStreaming = enableHighVolumeStreaming,
                    ValidateWorkingFileAfterWrite = validateAfterWrite,
                    ParquetRowGroupSize = parquetRowGroupSize
                }),
                NullLogger<ParquetIngestionWorkingFileStrategy>.Instance);
        }

        public Mock<IStorageService> Storage { get; }

        public ParquetIngestionWorkingFileStrategy Strategy { get; }

        public byte[]? UploadedBytes { get; private set; }
    }
}
