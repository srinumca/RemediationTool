using RemediationTool.Application.Constants;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class ApplicationUtilityTests
{
    [Fact]
    public void BuildParquetPath_UsesStableUtcFolderAndRemovesExtension()
    {
        var createdAtUtc = new DateTime(2026, 7, 21, 10, 30, 0, DateTimeKind.Utc);

        var path = IngestionWorkingFilePathBuilder.BuildParquetPath(
            "ING-20260721-ABC12345",
            "reports/source.data.csv",
            createdAtUtc);

        Assert.Equal(
            "ingestion-working/2026/07/21/ING-20260721-ABC12345/source.data.parquet",
            path);
        Assert.DoesNotContain('\\', path);
    }

    [Theory]
    [InlineData(".csv")]
    [InlineData("   .xlsx")]
    public void BuildParquetPath_UsesFallbackForBlankBaseName(string inboundFileName)
    {
        var path = IngestionWorkingFilePathBuilder.BuildParquetPath(
            "job-1",
            inboundFileName,
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.EndsWith("/ingestion-working.parquet", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiResponse_Ok_PopulatesSuccessEnvelope()
    {
        var before = DateTime.UtcNow;

        var response = ApiResponse<int>.Ok(42, "completed", "corr-1");

        var after = DateTime.UtcNow;
        Assert.True(response.Success);
        Assert.Equal("completed", response.Message);
        Assert.Equal(42, response.Data);
        Assert.Equal("corr-1", response.CorrelationId);
        Assert.InRange(response.TimestampUtc, before, after);
    }

    [Fact]
    public void ApiResponse_Fail_DoesNotPopulateData()
    {
        var response = ApiResponse<string>.Fail("failed", "corr-2");

        Assert.False(response.Success);
        Assert.Equal("failed", response.Message);
        Assert.Null(response.Data);
        Assert.Equal("corr-2", response.CorrelationId);
    }

    [Fact]
    public void PagedResult_ReportsCountAndContinuationState()
    {
        var firstPage = new PagedResult<string>
        {
            Items = new[] { "a", "b" },
            NextPageKey = "next-token"
        };
        var finalPage = new PagedResult<string>
        {
            Items = Array.Empty<string>()
        };

        Assert.Equal(2, firstPage.Count);
        Assert.False(firstPage.IsLastPage);
        Assert.Equal("next-token", firstPage.NextPageKey);
        Assert.Equal(0, finalPage.Count);
        Assert.True(finalPage.IsLastPage);
    }
}
