using Xunit;
using RemediationTool.Application.Options;
using RemediationTool.Application.Services;

namespace RemediationTool.Infrastructure.Tests;

public class IngestionResumeStoreCoordinatorTests
{
    [Fact]
    public async Task PrepareAsync_ParquetPrimaryAndSuccessful_SkipsStaging()
    {
        var options = CreateOptions();
        var parquetCalls = 0;
        var stagingCalls = 0;

        var result = await IngestionResumeStoreCoordinator.PrepareAsync(
            options,
            validRecordCount: 70000,
            createParquetAsync: () =>
            {
                parquetCalls++;
                return Task.CompletedTask;
            },
            writeStaging: () => stagingCalls++,
            clearParquetMetadata: () => { });

        Assert.True(result.ParquetReady);
        Assert.False(result.StagingWritten);
        Assert.Null(result.ParquetFailure);
        Assert.Equal(1, parquetCalls);
        Assert.Equal(0, stagingCalls);
    }

    [Fact]
    public async Task PrepareAsync_ParquetFailureWithFallback_WritesStaging()
    {
        var options = CreateOptions();
        var stagingCalls = 0;
        var clearCalls = 0;

        var result = await IngestionResumeStoreCoordinator.PrepareAsync(
            options,
            validRecordCount: 70000,
            createParquetAsync: () => throw new IOException("Parquet unavailable"),
            writeStaging: () => stagingCalls++,
            clearParquetMetadata: () => clearCalls++);

        Assert.False(result.ParquetReady);
        Assert.True(result.StagingWritten);
        Assert.IsType<IOException>(result.ParquetFailure);
        Assert.Equal(1, stagingCalls);
        Assert.Equal(1, clearCalls);
    }

    [Fact]
    public async Task PrepareAsync_ParquetFailureWithoutFallback_PropagatesFailure()
    {
        var options = CreateOptions();
        options.LegacyStagingFallbackEnabled = false;
        var stagingCalls = 0;

        await Assert.ThrowsAsync<IOException>(() =>
            IngestionResumeStoreCoordinator.PrepareAsync(
                options,
                validRecordCount: 70000,
                createParquetAsync: () => throw new IOException("Parquet unavailable"),
                writeStaging: () => stagingCalls++,
                clearParquetMetadata: () => { }));

        Assert.Equal(0, stagingCalls);
    }

    [Fact]
    public async Task PrepareAsync_LegacyDualStoreMode_WritesParquetAndStaging()
    {
        var options = CreateOptions();
        options.UseParquetAsPrimaryResumeStore = false;
        var stagingCalls = 0;

        var result = await IngestionResumeStoreCoordinator.PrepareAsync(
            options,
            validRecordCount: 70000,
            createParquetAsync: () => Task.CompletedTask,
            writeStaging: () => stagingCalls++,
            clearParquetMetadata: () => { });

        Assert.True(result.ParquetReady);
        Assert.True(result.StagingWritten);
        Assert.Equal(1, stagingCalls);
    }

    [Fact]
    public async Task PrepareAsync_NoValidRows_DoesNotCreateResumeArtifacts()
    {
        var options = CreateOptions();
        var parquetCalls = 0;
        var stagingCalls = 0;

        var result = await IngestionResumeStoreCoordinator.PrepareAsync(
            options,
            validRecordCount: 0,
            createParquetAsync: () =>
            {
                parquetCalls++;
                return Task.CompletedTask;
            },
            writeStaging: () => stagingCalls++,
            clearParquetMetadata: () => { });

        Assert.False(result.ParquetReady);
        Assert.False(result.StagingWritten);
        Assert.Equal(0, parquetCalls);
        Assert.Equal(0, stagingCalls);
    }

    private static IngestionProcessingOptions CreateOptions()
        => new()
        {
            EnableParquetWorkingFile = true,
            UseParquetAsPrimaryResumeStore = true,
            LegacyStagingFallbackEnabled = true
        };
}
