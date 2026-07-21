using RemediationTool.Application.Services;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class IngestionArchivePathBuilderTests
{
    private static readonly DateTime UploadedAtUtc =
        new(2026, 7, 21, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildFolderPrefix_UsesYearMonthAndReportUid()
    {
        var result = IngestionArchivePathBuilder.BuildFolderPrefix(
            "ING-20260721-001",
            UploadedAtUtc);

        Assert.Equal("2026/07/ING-20260721-001/", result);
    }

    [Fact]
    public void BuildOriginalFilePath_SanitizesDirectorySeparators()
    {
        var result = IngestionArchivePathBuilder.BuildOriginalFilePath(
            "ING-20260721-001",
            "nested/report.csv",
            UploadedAtUtc);

        Assert.Equal("2026/07/ING-20260721-001/nested_report.csv", result);
    }

    [Fact]
    public void BuildOriginalFilePath_UsesFallbackForBlankName()
    {
        var result = IngestionArchivePathBuilder.BuildOriginalFilePath(
            "ING-20260721-001",
            "   ",
            UploadedAtUtc);

        Assert.Equal("2026/07/ING-20260721-001/uploaded-file", result);
    }

    [Fact]
    public void BuildProcessingSummaryPath_UsesStableMetadataFileName()
    {
        var result = IngestionArchivePathBuilder.BuildProcessingSummaryPath(
            "ING-20260721-001",
            UploadedAtUtc);

        Assert.Equal(
            "2026/07/ING-20260721-001/report-metadata.json",
            result);
    }
}
