using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
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
    public void FindingType_ResolversAndInitialAssignment_ReturnExpectedValues(
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

        var finding = new FileFinding
        {
            FindingType = findingType!
        };

        Assert.Equal(expectedStatus, finding.Status);
        Assert.Equal(expectedStoredValue, finding.StatusColumnValue);
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

    [Theory]
    [InlineData(IngestionJobStatus.Started, 10, 0, false, false)]
    [InlineData(IngestionJobStatus.Failed, 10, 10, false, false)]
    [InlineData(IngestionJobStatus.Failed, 10, 5, false, true)]
    [InlineData(IngestionJobStatus.Failed, 0, 0, true, true)]
    public void IngestionCheckpoint_ResumeEligibility_UsesStatusProgressAndExplicitOverride(
        IngestionJobStatus status,
        int successCount,
        int processedCount,
        bool explicitValue,
        bool expected)
    {
        var checkpoint = new IngestionCheckpoint
        {
            Status = status,
            SuccessCount = successCount,
            LastProcessedRecordCount = processedCount,
            IsResumeEligible = explicitValue
        };

        Assert.Equal(expected, checkpoint.IsResumeEligible);
    }
}
