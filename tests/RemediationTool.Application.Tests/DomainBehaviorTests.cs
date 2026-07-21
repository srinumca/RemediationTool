using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class DomainBehaviorTests
{
    public static TheoryData<string?, FileStatus, string> InitialStatusCases => new()
    {
        { null, FileStatus.NotYetStarted, FileStatus.NotYetStarted.ToString() },
        { "", FileStatus.NotYetStarted, FileStatus.NotYetStarted.ToString() },
        { "Obsolete", FileStatus.NotYetStarted, FileStatus.NotYetStarted.ToString() },
        { " obsolete ", FileStatus.NotYetStarted, FileStatus.NotYetStarted.ToString() },
        { "Quarantined", FileStatus.Quarantined, "Quarantined" },
        { "Total Pending Quarantined", FileStatus.TotalPendingQuarantined, "Total Pending Quarantined" },
        { "total_pending_quarantined", FileStatus.TotalPendingQuarantined, "total_pending_quarantined" },
        { "Restoration", FileStatus.Restoration, "Restoration" },
        { "Restored", FileStatus.Restored, "Restored" },
        { "Deleted", FileStatus.Deleted, "Deleted" },
        { "Exception", FileStatus.Exception, "Exception" },
        { "Exclusion", FileStatus.Exclusion, "Exclusion" },
        { "Not-Obsolete", FileStatus.NotObsolete, "Not-Obsolete" },
        { "Error", FileStatus.Error, "Error" },
        { "FutureType", FileStatus.NotYetStarted, "FutureType" }
    };

    [Theory]
    [MemberData(nameof(InitialStatusCases))]
    public void FindingType_InitialAssignment_SetsExpectedWorkflowAndStoredStatus(
        string? findingType,
        FileStatus expectedStatus,
        string expectedStoredValue)
    {
        var finding = new FileFinding
        {
            FindingType = findingType!
        };

        Assert.Equal(expectedStatus, finding.Status);
        Assert.Equal(expectedStoredValue, finding.StatusColumnValue);
    }

    [Theory]
    [MemberData(nameof(InitialStatusCases))]
    public void StaticStatusResolvers_NormalizeExpectedValues(
        string? findingType,
        FileStatus expectedStatus,
        string expectedStoredValue)
    {
        Assert.Equal(
            expectedStatus,
            FileFinding.ResolveInitialStatusFromFindingType(findingType));
        Assert.Equal(
            expectedStoredValue,
            FileFinding.ResolveInitialStatusColumnValueFromFindingType(findingType));
    }

    [Theory]
    [InlineData(null, FileStatus.NotYetStarted)]
    [InlineData("", FileStatus.NotYetStarted)]
    [InlineData("Quarantined", FileStatus.Quarantined)]
    [InlineData("not obsolete", FileStatus.NotObsolete)]
    [InlineData("TOTAL_PENDING_QUARANTINED", FileStatus.TotalPendingQuarantined)]
    [InlineData("unknown", FileStatus.NotYetStarted)]
    public void ResolveStatusFromStoredValue_HandlesEnumLegacyAndUnknownValues(
        string? storedValue,
        FileStatus expected)
    {
        Assert.Equal(expected, FileFinding.ResolveStatusFromStoredValue(storedValue));
    }

    [Fact]
    public void FindingType_DoesNotOverwriteLifecycleStatusAfterProcessingStarts()
    {
        var finding = new FileFinding
        {
            FindingType = "Obsolete"
        };
        finding.Status = FileStatus.QuarantineComplete;

        finding.FindingType = "Restoration";

        Assert.Equal(FileStatus.QuarantineComplete, finding.Status);
        Assert.Equal(FileStatus.QuarantineComplete.ToString(), finding.StatusColumnValue);
    }

    [Fact]
    public void StatusSetter_AlwaysKeepsStatusColumnValueInSync()
    {
        var finding = new FileFinding();

        finding.Status = FileStatus.Deleted;

        Assert.Equal(FileStatus.Deleted, finding.Status);
        Assert.Equal("Deleted", finding.StatusColumnValue);
    }

    [Fact]
    public void CompatibilityAliases_RoundTripUnderlyingProperties()
    {
        var loadDate = new DateTime(2026, 7, 21, 1, 2, 3, DateTimeKind.Utc);
        var updateDate = loadDate.AddMinutes(1);
        var modifiedDate = loadDate.AddDays(-1);
        var quarantineDate = loadDate.AddHours(1);
        var finding = new FileFinding
        {
            ErrorReason = "reason",
            FileName = "file.txt",
            FilePath = "/source/file.txt",
            SourceSystem = "SMB",
            FileSize = 123,
            LastModifiedDate = modifiedDate,
            IngestionId = "job-1",
            UploadedBy = "user-1",
            LoadDate = loadDate,
            UpdatedDate = updateDate,
            QuarantineDate = quarantineDate,
            DataSystem = "SharePoint"
        };

        Assert.Equal("reason", finding.IngestionErrorReason);
        Assert.Equal("file.txt", finding.FindingFileName);
        Assert.Equal("/source/file.txt", finding.CurrentFileLocation);
        Assert.Equal(123, finding.FindingFileSizeBytes);
        Assert.Equal(modifiedDate, finding.LastModifiedDateUtc);
        Assert.Equal("job-1", finding.IngestionJobId);
        Assert.Equal("user-1", finding.UserName);
        Assert.Equal(loadDate, finding.LoadDateUtc);
        Assert.Equal(updateDate, finding.LastUpdateDateUtc);
        Assert.Equal(quarantineDate, finding.QuarantineDateUtc);
        Assert.Equal("SharePoint", finding.OriginatingDataSystem);
    }

    [Fact]
    public void CompatibilityAliases_ApplySafeDefaultsForNullValues()
    {
        var finding = new FileFinding
        {
            ErrorReason = null!,
            FileName = null!,
            FilePath = null!,
            SourceSystem = null!,
            UploadedBy = null!,
            QuarantinePath = null,
            DataSystem = null!
        };

        Assert.Equal(string.Empty, finding.ErrorReason);
        Assert.Equal(string.Empty, finding.FileName);
        Assert.Equal(string.Empty, finding.FilePath);
        Assert.Equal(string.Empty, finding.SourceSystem);
        Assert.Equal("System", finding.UploadedBy);
        Assert.Equal(string.Empty, finding.CurrentFileLocation);
        Assert.Equal(string.Empty, finding.DataSystem);
        Assert.Equal(0, finding.FileSize);
        Assert.Equal(DateTime.MinValue, finding.LastModifiedDate);
    }

    [Fact]
    public void QuarantinePath_IsVisibleOnlyAfterQuarantineCompletes()
    {
        var finding = new FileFinding
        {
            CurrentFileLocation = "/quarantine/file.txt",
            Status = FileStatus.Quarantined
        };

        Assert.Null(finding.QuarantinePath);

        finding.Status = FileStatus.QuarantineComplete;

        Assert.Equal("/quarantine/file.txt", finding.QuarantinePath);
    }

    [Fact]
    public void RejectedRowCompatibilityAliases_RoundTripPrimaryFields()
    {
        var createdAt = new DateTime(2026, 7, 21, 4, 5, 6, DateTimeKind.Utc);
        var row = new RejectedRowDetail
        {
            RejectedRowId = "row-1",
            JobId = "job-1",
            CreatedAtUtc = createdAt
        };

        Assert.Equal("row-1", row.Id);
        Assert.Equal("row-1", row.RejectedRowId);
        Assert.Equal("job-1", row.Uid);
        Assert.Equal("job-1", row.JobId);
        Assert.Equal(createdAt, row.ErrorDateUtc);
        Assert.Equal(createdAt, row.CreatedAtUtc);
        Assert.Equal("Error", row.Status);
    }

    [Theory]
    [InlineData(FileStatus.Started, 10, 0, false, false)]
    [InlineData(FileStatus.Failed, 10, 10, false, false)]
    [InlineData(FileStatus.Failed, 10, 5, false, true)]
    [InlineData(FileStatus.Failed, 0, 0, true, true)]
    public void IngestionCheckpoint_ResumeEligibility_UsesStatusProgressAndExplicitOverride(
        FileStatusAdapter statusAdapter,
        int successCount,
        int processedCount,
        bool explicitValue,
        bool expected)
    {
        var checkpoint = new IngestionCheckpoint
        {
            Status = statusAdapter.ToIngestionStatus(),
            SuccessCount = successCount,
            LastProcessedRecordCount = processedCount,
            IsResumeEligible = explicitValue
        };

        Assert.Equal(expected, checkpoint.IsResumeEligible);
    }

    public enum FileStatusAdapter
    {
        Started,
        Failed
    }
}

internal static class FileStatusAdapterExtensions
{
    public static RemediationTool.Domain.Enum.IngestionJobStatus ToIngestionStatus(
        this DomainBehaviorTests.FileStatusAdapter value)
        => value == DomainBehaviorTests.FileStatusAdapter.Failed
            ? RemediationTool.Domain.Enum.IngestionJobStatus.Failed
            : RemediationTool.Domain.Enum.IngestionJobStatus.Started;
}
