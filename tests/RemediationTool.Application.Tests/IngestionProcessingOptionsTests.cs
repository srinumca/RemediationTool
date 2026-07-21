using RemediationTool.Application.Options;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class IngestionProcessingOptionsTests
{
    [Theory]
    [InlineData(-1, 4, 4)]
    [InlineData(0, 7, 7)]
    [InlineData(1, 4, 1)]
    [InlineData(8, 4, 8)]
    [InlineData(50, 4, 16)]
    public void ResolveDynamoDbWriteConcurrency_UsesFallbackAndClampsRange(
        int configuredConcurrency,
        int legacyConcurrency,
        int expected)
    {
        var options = new IngestionProcessingOptions
        {
            DynamoDbWriteConcurrency = configuredConcurrency,
            DynamoDbMaxConcurrentBatchWrites = legacyConcurrency
        };

        Assert.Equal(expected, options.ResolveDynamoDbWriteConcurrency());
    }

    [Theory]
    [InlineData(1, 10000, 25)]
    [InlineData(2500, 10000, 2500)]
    [InlineData(20000, 10000, 10000)]
    public void ResolveRejectedRowBatchSize_ClampsToSupportedRange(
        int configuredBatchSize,
        int maxBatchSize,
        int expected)
    {
        var options = new IngestionProcessingOptions
        {
            RejectedRowBatchSize = configuredBatchSize,
            MaxBatchSize = maxBatchSize
        };

        Assert.Equal(expected, options.ResolveRejectedRowBatchSize());
    }

    [Fact]
    public void MaxUploadFileSizeBytes_ConvertsMegabytesWithoutOverflow()
    {
        var options = new IngestionProcessingOptions
        {
            MaxUploadFileSizeMb = 500
        };

        Assert.Equal(524_288_000L, options.MaxUploadFileSizeBytes);
    }
}
