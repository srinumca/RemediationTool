using Amazon.DynamoDBv2.Model;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Infrastructure.DynamoDB;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class DynamoDbAttributeMapTests
{
    [Fact]
    public void FileFinding_CurrentSchema_RoundTripsMappedFields()
    {
        var id = Guid.NewGuid();
        var loadDate = new DateTime(2026, 7, 21, 1, 2, 3, DateTimeKind.Utc);
        var modified = loadDate.AddDays(-2);
        var finding = new FileFinding
        {
            Id = id,
            RecordVersionId = "7",
            SourceRecordId = "source-row-1",
            IngestionJobId = "ING-1",
            InboundFileName = "report.csv",
            UserName = "user@example.com",
            LoadDateUtc = loadDate,
            LastUpdateDateUtc = loadDate.AddMinutes(5),
            FindingFileName = "file.txt",
            FindingFileFormat = "txt",
            FindingFileSizeBytes = 12345,
            CurrentFileLocation = @"\\server\share\file.txt",
            FindingType = "Quarantined",
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG",
            SourceSystemPlatform = "NetApp",
            ErrorCategory = "TimeoutException",
            LastModifiedDateUtc = modified,
            CreatedDateUtc = modified.AddDays(-1),
            LastAccessedDateUtc = modified.AddHours(1),
            DetectionDateUtc = modified.AddHours(2),
            SiteOwner = "site-owner",
            FileOwner = "file-owner",
            RiskLevel = "High",
            OriginalFileLocation = @"\\server\original\file.txt",
            QuarantineDateUtc = loadDate.AddHours(1),
            RestoredDateUtc = loadDate.AddHours(2),
            DeletedDateUtc = loadDate.AddHours(3),
            RestorationTicketIdentifier = "INC-1",
            RestorationRequestorEmail = "requestor@example.com",
            RestorationComment = "restore",
            Status = FileStatus.QuarantineComplete,
            ErrorReason = "none"
        };

        var map = DynamoDbAttributeMap.ToMap(finding);
        var roundTrip = DynamoDbAttributeMap.ToFileFinding(map);

        Assert.Equal(id.ToString(), map["id"].S);
        Assert.Equal("ING-1", map["uid"].S);
        Assert.Equal("7", map["recordVersionId"].N);
        Assert.Equal(loadDate.ToString("o"), map["rowCreatedDateOn"].S);
        Assert.Equal(modified.ToString("o"), map["fileLastModifiedOn"].S);
        Assert.False(map.ContainsKey("jobId"));
        Assert.False(map.ContainsKey("loadDateUtc"));

        Assert.Equal(id, roundTrip.Id);
        Assert.Equal("7", roundTrip.RecordVersionId);
        Assert.Equal("source-row-1", roundTrip.SourceRecordId);
        Assert.Equal("ING-1", roundTrip.IngestionJobId);
        Assert.Equal("file.txt", roundTrip.FindingFileName);
        Assert.Equal(12345, roundTrip.FindingFileSizeBytes);
        Assert.Equal("Quarantined", roundTrip.FindingType);
        Assert.Equal("NetApp", roundTrip.SourceSystemPlatform);
        Assert.Equal("TimeoutException", roundTrip.ErrorCategory);
        Assert.Equal(modified, roundTrip.LastModifiedDateUtc);
        Assert.Equal(FileStatus.QuarantineComplete, roundTrip.Status);
        Assert.Equal(FileStatus.QuarantineComplete.ToString(), roundTrip.StatusColumnValue);
        Assert.Equal("none", roundTrip.ErrorReason);
    }

    [Fact]
    public void FileFinding_NonNumericVersion_WritesSafeNumericFallback()
    {
        var finding = new FileFinding
        {
            RecordVersionId = "legacy-guid-value",
            FindingType = "Obsolete"
        };

        var map = DynamoDbAttributeMap.ToMap(finding);

        Assert.Equal("1", map["recordVersionId"].N);
    }

    [Fact]
    public void FileFinding_LegacySchema_UsesFallbackAttributesAndCapitalStatus()
    {
        var loadDate = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var modified = loadDate.AddDays(-1);
        var map = new Dictionary<string, AttributeValue>
        {
            ["id"] = new() { S = Guid.NewGuid().ToString() },
            ["recordVersionId"] = new() { S = "legacy-version" },
            ["jobId"] = new() { S = "legacy-job" },
            ["loadDateUtc"] = new() { S = loadDate.ToString("o") },
            ["lastModifiedDateUtc"] = new() { S = modified.ToString("o") },
            ["findingType"] = new() { S = "Not Obsolete" },
            ["Status"] = new() { S = "Not Obsolete" }
        };

        var finding = DynamoDbAttributeMap.ToFileFinding(map);

        Assert.Equal("legacy-version", finding.RecordVersionId);
        Assert.Equal("legacy-job", finding.IngestionJobId);
        Assert.Equal(loadDate, finding.LoadDateUtc);
        Assert.Equal(modified, finding.LastModifiedDateUtc);
        Assert.Equal(FileStatus.NotObsolete, finding.Status);
        Assert.Equal("Not Obsolete", finding.StatusColumnValue);
    }

    [Fact]
    public void JobAudit_CurrentSchema_RoundTripsCountsFlagsAndJsonTypeCounts()
    {
        var started = new DateTime(2026, 7, 21, 5, 0, 0, DateTimeKind.Utc);
        var audit = new IngestionJobAudit
        {
            JobId = "ING-2",
            ReportUid = "ING-2",
            InboundFileName = "report.csv",
            FileSizeBytes = 9988,
            InboundFileContentType = "text/csv",
            FileFormat = "csv",
            S3FolderPath = "2026/07/ING-2/",
            SourceFilePath = "2026/07/ING-2/report.csv",
            MetadataJsonPath = "2026/07/ING-2/report-metadata.json",
            WorkingFilePath = "working/report.parquet",
            WorkingFileFormat = "Parquet",
            WorkingFileRecordCount = 10,
            UploadedBy = "uploader",
            UserName = "user",
            StartedBy = "starter",
            UploadedDisplayName = "Display Name",
            UploadedEmailId = "user@example.com",
            InboundFileChecksum = "abc123",
            StartTimestampUtc = started,
            EndTimestampUtc = started.AddMinutes(1),
            Status = IngestionJobStatus.Success,
            SourceSystem = "SMB",
            TriggerType = "Manual",
            IngestionMode = "Full",
            PayloadRecordCount = 12,
            TotalRecords = 12,
            SuccessCount = 10,
            RejectCount = 2,
            ValidationFailureCount = 2,
            FindingTypeCounts = new Dictionary<string, int>
            {
                ["Obsolete"] = 8,
                ["Quarantined"] = 2
            },
            BatchSize = 5,
            TotalBatches = 2,
            PersistedBatchCount = 2,
            LastSuccessfulBatchNumber = 2,
            LastProcessedRecordCount = 10,
            CheckpointingEnabled = true,
            BatchPersistenceRetryCount = 1,
            MaxBatchPersistenceRetryCount = 3,
            IsResumeEligible = false,
            LastCheckpointUtc = started.AddSeconds(30),
            CheckpointMessage = "completed"
        };

        var map = DynamoDbAttributeMap.ToMap(audit);
        var roundTrip = DynamoDbAttributeMap.ToIngestionJobAudit(map);

        Assert.Equal("9988", map["inboundFileSizeBytes"].N);
        Assert.Equal("2026/07/ING-2/report.csv", map["s3FilePath"].S);
        Assert.StartsWith("{", map["findingTypeCounts"].S, StringComparison.Ordinal);
        Assert.Null(map["findingTypeCounts"].M);
        Assert.Equal(audit.JobId, roundTrip.JobId);
        Assert.Equal(audit.ReportUid, roundTrip.ReportUid);
        Assert.Equal(audit.FileSizeBytes, roundTrip.FileSizeBytes);
        Assert.Equal(audit.SourceFilePath, roundTrip.SourceFilePath);
        Assert.Equal(audit.MetadataJsonPath, roundTrip.MetadataJsonPath);
        Assert.Equal(audit.Status, roundTrip.Status);
        Assert.Equal(audit.SuccessCount, roundTrip.SuccessCount);
        Assert.Equal(audit.RejectCount, roundTrip.RejectCount);
        Assert.Equal(audit.FindingTypeCounts, roundTrip.FindingTypeCounts);
        Assert.True(roundTrip.CheckpointingEnabled);
        Assert.False(roundTrip.IsResumeEligible);
        Assert.Equal("completed", roundTrip.CheckpointMessage);
    }

    [Fact]
    public void JobAudit_LegacySchema_UsesOldNamesAndMapTypeCounts()
    {
        var map = new Dictionary<string, AttributeValue>
        {
            ["jobId"] = new() { S = "legacy-job" },
            ["reportUid"] = new() { S = "legacy-report" },
            ["fileSizeBytes"] = new() { N = "123" },
            ["sourceFilePath"] = new() { S = "old/source.csv" },
            ["metadataJsonPath"] = new() { S = "old/metadata.json" },
            ["uploadedBy"] = new() { S = "legacy-user" },
            ["status"] = new() { S = "Failed" },
            ["findingTypeCounts"] = new()
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["Obsolete"] = new() { N = "3" },
                    ["Error"] = new() { N = "1" }
                }
            }
        };

        var audit = DynamoDbAttributeMap.ToIngestionJobAudit(map);

        Assert.Equal("legacy-report", audit.ReportUid);
        Assert.Equal(123, audit.FileSizeBytes);
        Assert.Equal("old/source.csv", audit.SourceFilePath);
        Assert.Equal("old/metadata.json", audit.MetadataJsonPath);
        Assert.Equal("legacy-user", audit.UploadedBy);
        Assert.Equal(IngestionJobStatus.Failed, audit.Status);
        Assert.Equal(3, audit.FindingTypeCounts["Obsolete"]);
        Assert.Equal(1, audit.FindingTypeCounts["Error"]);
    }

    [Fact]
    public void RejectedRow_CurrentSchema_RoundTripsValidationAndInfrastructureFields()
    {
        var errorDate = new DateTime(2026, 7, 21, 7, 0, 0, DateTimeKind.Utc);
        var row = new RejectedRowDetail
        {
            Id = "row-1",
            Uid = "job-1",
            InboundFileName = "report.csv",
            SourceRecordId = "source-1",
            FindingFileName = "file.txt",
            FindingType = null,
            UserName = "user",
            CurrentFileLocation = "/source/file.txt",
            DataSystem = "NetApp",
            FileOwner = "owner",
            SiteOwner = "site",
            FindingFileFormat = "txt",
            FindingFileSizeBytes = 100,
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG",
            QuarantineDate = "2026-07-21",
            Status = "Error",
            RecordVersionId = 4,
            ErrorDateUtc = errorDate,
            FileLastModifiedOn = errorDate.AddDays(-1),
            ErrorCategory = "ValidationError",
            StackTrace = "stack",
            RowNumber = 12,
            FieldName = "FindingType",
            RejectedValue = "Bad",
            ErrorReason = "invalid",
            RawRowJson = "{}"
        };

        var map = DynamoDbAttributeMap.ToMap(row);
        var roundTrip = DynamoDbAttributeMap.ToRejectedRowDetail(map);

        Assert.Equal("row-1", map["id"].S);
        Assert.Equal("job-1", map["uid"].S);
        Assert.Equal("Error", map["findingType"].S);
        Assert.Equal("Error", map["Status"].S);
        Assert.Equal("100", map["findingFileSizeBytes"].N);
        Assert.Equal("row-1", roundTrip.Id);
        Assert.Equal("job-1", roundTrip.Uid);
        Assert.Equal("Error", roundTrip.FindingType);
        Assert.Equal(12, roundTrip.RowNumber);
        Assert.Equal("FindingType", roundTrip.FieldName);
        Assert.Equal("ValidationError", roundTrip.ErrorCategory);
        Assert.Equal(errorDate, roundTrip.ErrorDateUtc);
    }

    [Fact]
    public void RejectedRow_LegacyJobAndLowercaseStatus_AreSupported()
    {
        var map = new Dictionary<string, AttributeValue>
        {
            ["jobId"] = new() { S = "legacy-job" },
            ["status"] = new() { S = "Error" },
            ["rowNumber"] = new() { N = "5" }
        };

        var row = DynamoDbAttributeMap.ToRejectedRowDetail(map);

        Assert.Equal("legacy-job", row.Uid);
        Assert.Equal("Error", row.Status);
        Assert.Equal(5, row.RowNumber);
        Assert.False(string.IsNullOrWhiteSpace(row.Id));
    }

    [Fact]
    public void Checkpoint_RoundTripsAllResumeFields()
    {
        var created = new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc);
        var checkpoint = new IngestionCheckpoint
        {
            JobId = "job-3",
            InboundFileName = "report.csv",
            UserName = "user",
            SourceSystem = "SMB",
            TriggerType = "Manual",
            IngestionMode = "Full",
            BatchSize = 100,
            TotalBatches = 4,
            LastSuccessfulBatchNumber = 2,
            LastProcessedRecordCount = 200,
            PersistedBatchCount = 2,
            SuccessCount = 350,
            RejectCount = 10,
            BatchPersistenceRetryCount = 3,
            Status = IngestionJobStatus.Failed,
            IsResumeEligible = true,
            CreatedAtUtc = created,
            LastCheckpointUtc = created.AddMinutes(1),
            FailureReason = "DynamoDB throttled",
            WorkingFilePath = "working/report.parquet",
            WorkingFileFormat = "Parquet",
            WorkingFileRecordCount = 350
        };

        var map = DynamoDbAttributeMap.ToMap(checkpoint);
        var roundTrip = DynamoDbAttributeMap.ToIngestionCheckpoint(map);

        Assert.Equal("job-3", map["jobId"].S);
        Assert.True(map["isResumeEligible"].BOOL);
        Assert.Equal(checkpoint.JobId, roundTrip.JobId);
        Assert.Equal(checkpoint.Status, roundTrip.Status);
        Assert.True(roundTrip.IsResumeEligible);
        Assert.Equal(checkpoint.LastProcessedRecordCount, roundTrip.LastProcessedRecordCount);
        Assert.Equal(checkpoint.WorkingFilePath, roundTrip.WorkingFilePath);
        Assert.Equal(checkpoint.WorkingFileRecordCount, roundTrip.WorkingFileRecordCount);
        Assert.Equal(checkpoint.FailureReason, roundTrip.FailureReason);
    }
}
