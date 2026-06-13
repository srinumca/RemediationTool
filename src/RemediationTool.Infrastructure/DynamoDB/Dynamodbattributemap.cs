using Amazon.DynamoDBv2.Model;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Infrastructure.DynamoDB;

/// <summary>
/// Maps domain entities to/from DynamoDB attribute dictionaries.
/// All DynamoDB attribute names are camelCase per GFR naming standards.
/// </summary>
public static class DynamoDbAttributeMap
{
    // =====================================================================
    // Helper methods
    // =====================================================================

    private static void AddS(Dictionary<string, AttributeValue> item, string key, string? value)
    {
        if (value is not null)
            item[key] = new AttributeValue { S = value };
    }

    private static void AddN(Dictionary<string, AttributeValue> item, string key, long value)
    {
        item[key] = new AttributeValue { N = value.ToString() };
    }

    private static void AddNullableN(Dictionary<string, AttributeValue> item, string key, long? value)
    {
        if (value.HasValue)
            item[key] = new AttributeValue { N = value.Value.ToString() };
    }

    private static void AddDate(Dictionary<string, AttributeValue> item, string key, DateTime value)
    {
        item[key] = new AttributeValue { S = value.ToString("o") };
    }

    private static void AddNullableDate(Dictionary<string, AttributeValue> item, string key, DateTime? value)
    {
        if (value.HasValue)
            item[key] = new AttributeValue { S = value.Value.ToString("o") };
    }

    private static void AddBool(Dictionary<string, AttributeValue> item, string key, bool value)
    {
        item[key] = new AttributeValue { BOOL = value };
    }

    private static void AddIntMap(Dictionary<string, AttributeValue> item, string key, Dictionary<string, int>? value)
    {
        if (value is null || value.Count == 0)
            return;

        item[key] = new AttributeValue
        {
            M = value.ToDictionary(
                kvp => kvp.Key,
                kvp => new AttributeValue { N = kvp.Value.ToString() })
        };
    }

    private static string? GetS(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) ? v.S : null;

    private static string GetSOrEmpty(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) ? (v.S ?? string.Empty) : string.Empty;

    private static long? GetNullableLong(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.N is not null ? long.Parse(v.N) : null;

    private static int GetIntOrZero(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.N is not null ? int.Parse(v.N) : 0;

    private static bool GetBoolOrFalse(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.BOOL is true;

    private static DateTime GetDateOrDefault(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.S is not null ? DateTime.Parse(v.S).ToUniversalTime() : default;

    private static DateTime? GetNullableDate(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.S is not null ? DateTime.Parse(v.S).ToUniversalTime() : null;

    private static Dictionary<string, int> GetIntMap(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var v) || v.M is null)
            return new Dictionary<string, int>();

        return v.M.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.N is not null ? int.Parse(kvp.Value.N) : 0);
    }

    // =====================================================================
    // FileFinding  <->  gfr-file-findings-dev
    // =====================================================================

    public static class FileFindingAttributes
    {
        public const string Id = "id";
        public const string RecordVersionId = "recordVersionId";
        public const string SourceRecordId = "sourceRecordId";
        public const string JobId = "jobId"; // maps from FileFinding.IngestionJobId
        public const string InboundFileName = "inboundFileName";
        public const string UserName = "userName";
        public const string LoadDateUtc = "loadDateUtc";
        public const string LastUpdateDateUtc = "lastUpdateDateUtc";
        public const string FindingFileName = "findingFileName";
        public const string FindingFileFormat = "findingFileFormat";
        public const string FindingFileSizeBytes = "findingFileSizeBytes";
        public const string CurrentFileLocation = "currentFileLocation";
        public const string FindingType = "findingType";
        public const string DataSystem = "dataSystem";
        public const string OriginatingDataSystem = "originatingDataSystem";
        public const string OriginatingVendorTool = "originatingVendorTool";
        public const string OriginalFileLocation = "originalFileLocation";
        public const string QuarantineDateUtc = "quarantineDateUtc";
        public const string RestorationDateUtc = "restorationDateUtc";
        public const string ExceptionDateUtc = "exceptionDateUtc";
        public const string DeletionDateUtc = "deletionDateUtc";
        public const string SiteOwner = "siteOwner";
        public const string FileOwner = "fileOwner";
        public const string RestorationTicketIdentifier = "restorationTicketIdentifier";
        public const string RestorationRequestorEmail = "restorationRequestorEmail";
        public const string RestorationComment = "restorationComment";
        public const string ErrorCategory = "errorCategory";
        public const string ErrorDetail = "errorDetail";
    }

    public static Dictionary<string, AttributeValue> ToItem(FileFinding finding)
    {
        var item = new Dictionary<string, AttributeValue>();

        AddS(item, FileFindingAttributes.Id, finding.Id.ToString());
        AddS(item, FileFindingAttributes.RecordVersionId, finding.RecordVersionId);
        AddS(item, FileFindingAttributes.SourceRecordId, finding.SourceRecordId);
        AddS(item, FileFindingAttributes.JobId, finding.IngestionJobId);
        AddS(item, FileFindingAttributes.InboundFileName, finding.InboundFileName);
        AddS(item, FileFindingAttributes.UserName, finding.UserName);

        AddDate(item, FileFindingAttributes.LoadDateUtc, finding.LoadDateUtc);
        AddDate(item, FileFindingAttributes.LastUpdateDateUtc, finding.LastUpdateDateUtc);

        AddS(item, FileFindingAttributes.FindingFileName, finding.FindingFileName);
        AddS(item, FileFindingAttributes.FindingFileFormat, finding.FindingFileFormat);
        AddNullableN(item, FileFindingAttributes.FindingFileSizeBytes, finding.FindingFileSizeBytes);
        AddS(item, FileFindingAttributes.CurrentFileLocation, finding.CurrentFileLocation);
        AddS(item, FileFindingAttributes.FindingType, finding.FindingType);
        AddS(item, FileFindingAttributes.DataSystem, finding.DataSystem);
        AddS(item, FileFindingAttributes.OriginatingDataSystem, finding.OriginatingDataSystem);
        AddS(item, FileFindingAttributes.OriginatingVendorTool, finding.OriginatingVendorTool);
        AddS(item, FileFindingAttributes.OriginalFileLocation, finding.OriginalFileLocation);

        AddNullableDate(item, FileFindingAttributes.QuarantineDateUtc, finding.QuarantineDateUtc);
        AddNullableDate(item, FileFindingAttributes.RestorationDateUtc, finding.RestorationDateUtc);
        AddNullableDate(item, FileFindingAttributes.ExceptionDateUtc, finding.ExceptionDateUtc);
        AddNullableDate(item, FileFindingAttributes.DeletionDateUtc, finding.DeletionDateUtc);

        AddS(item, FileFindingAttributes.SiteOwner, finding.SiteOwner);
        AddS(item, FileFindingAttributes.FileOwner, finding.FileOwner);

        AddS(item, FileFindingAttributes.RestorationTicketIdentifier, finding.RestorationTicketIdentifier);
        AddS(item, FileFindingAttributes.RestorationRequestorEmail, finding.RestorationRequestorEmail);
        AddS(item, FileFindingAttributes.RestorationComment, finding.RestorationComment);

        AddS(item, FileFindingAttributes.ErrorCategory, finding.ErrorCategory.ToString());
        AddS(item, FileFindingAttributes.ErrorDetail, finding.ErrorDetail);

        return item;
    }

    public static FileFinding FromFileFindingItem(Dictionary<string, AttributeValue> item)
    {
        var errorCategoryRaw = GetS(item, FileFindingAttributes.ErrorCategory);
        Enum.TryParse<ErrorCategory>(errorCategoryRaw, ignoreCase: true, out var parsedErrorCategory);

        return new FileFinding
        {
            Id = Guid.TryParse(GetS(item, FileFindingAttributes.Id), out var id) ? id : Guid.NewGuid(),
            RecordVersionId = GetSOrEmpty(item, FileFindingAttributes.RecordVersionId),
            SourceRecordId = GetS(item, FileFindingAttributes.SourceRecordId),
            IngestionJobId = GetS(item, FileFindingAttributes.JobId),
            InboundFileName = GetSOrEmpty(item, FileFindingAttributes.InboundFileName),
            UserName = GetSOrEmpty(item, FileFindingAttributes.UserName),

            LoadDateUtc = GetDateOrDefault(item, FileFindingAttributes.LoadDateUtc),
            LastUpdateDateUtc = GetDateOrDefault(item, FileFindingAttributes.LastUpdateDateUtc),

            FindingFileName = GetSOrEmpty(item, FileFindingAttributes.FindingFileName),
            FindingFileFormat = GetSOrEmpty(item, FileFindingAttributes.FindingFileFormat),
            FindingFileSizeBytes = GetNullableLong(item, FileFindingAttributes.FindingFileSizeBytes),
            CurrentFileLocation = GetSOrEmpty(item, FileFindingAttributes.CurrentFileLocation),
            FindingType = GetSOrEmpty(item, FileFindingAttributes.FindingType),
            DataSystem = GetSOrEmpty(item, FileFindingAttributes.DataSystem),
            OriginatingDataSystem = GetSOrEmpty(item, FileFindingAttributes.OriginatingDataSystem),
            OriginatingVendorTool = GetSOrEmpty(item, FileFindingAttributes.OriginatingVendorTool),
            OriginalFileLocation = GetS(item, FileFindingAttributes.OriginalFileLocation),

            QuarantineDateUtc = GetNullableDate(item, FileFindingAttributes.QuarantineDateUtc),
            RestorationDateUtc = GetNullableDate(item, FileFindingAttributes.RestorationDateUtc),
            ExceptionDateUtc = GetNullableDate(item, FileFindingAttributes.ExceptionDateUtc),
            DeletionDateUtc = GetNullableDate(item, FileFindingAttributes.DeletionDateUtc),

            SiteOwner = GetS(item, FileFindingAttributes.SiteOwner),
            FileOwner = GetS(item, FileFindingAttributes.FileOwner),

            RestorationTicketIdentifier = GetS(item, FileFindingAttributes.RestorationTicketIdentifier),
            RestorationRequestorEmail = GetS(item, FileFindingAttributes.RestorationRequestorEmail),
            RestorationComment = GetS(item, FileFindingAttributes.RestorationComment),

            ErrorCategory = parsedErrorCategory,
            ErrorDetail = GetS(item, FileFindingAttributes.ErrorDetail)
        };
    }

    // =====================================================================
    // IngestionJobAudit  <->  gfr-file-metadata-dev
    // =====================================================================

    public static class FileMetadataAttributes
    {
        public const string JobId = "jobId";
        public const string InboundFileName = "inboundFileName";
        public const string UserName = "userName";
        public const string StartedBy = "startedBy";
        public const string StartTimestampUtc = "startTimestampUtc";
        public const string EndTimestampUtc = "endTimestampUtc";
        public const string SourceSystem = "sourceSystem";
        public const string TriggerType = "triggerType";
        public const string IngestionMode = "ingestionMode";
        public const string PayloadRecordCount = "payloadRecordCount";
        public const string TotalRecords = "totalRecords";
        public const string SuccessCount = "successCount";
        public const string RejectCount = "rejectCount";
        public const string ValidationFailureCount = "validationFailureCount";
        public const string FindingTypeCounts = "findingTypeCounts";
        public const string BatchSize = "batchSize";
        public const string TotalBatches = "totalBatches";
        public const string PersistedBatchCount = "persistedBatchCount";
        public const string LastSuccessfulBatchNumber = "lastSuccessfulBatchNumber";
        public const string LastProcessedRecordCount = "lastProcessedRecordCount";
        public const string CheckpointingEnabled = "checkpointingEnabled";
        public const string BatchPersistenceRetryCount = "batchPersistenceRetryCount";
        public const string MaxBatchPersistenceRetryCount = "maxBatchPersistenceRetryCount";
        public const string Status = "status";
        public const string ErrorMessage = "errorMessage";
        public const string FailureReason = "failureReason";
        public const string ArchivedFilePath = "archivedFilePath";
        public const string ProcessingSummaryPath = "processingSummaryPath";
        public const string IsResumeEligible = "isResumeEligible";
        public const string LastCheckpointUtc = "lastCheckpointUtc";
        public const string CheckpointMessage = "checkpointMessage";
        public const string WorkingFileFormat = "workingFileFormat";
        public const string WorkingFilePath = "workingFilePath";
        public const string WorkingFileRecordCount = "workingFileRecordCount";
    }

    public static Dictionary<string, AttributeValue> ToItem(IngestionJobAudit audit)
    {
        var item = new Dictionary<string, AttributeValue>();

        AddS(item, FileMetadataAttributes.JobId, audit.JobId);
        AddS(item, FileMetadataAttributes.InboundFileName, audit.InboundFileName);
        AddS(item, FileMetadataAttributes.UserName, audit.UserName);
        AddS(item, FileMetadataAttributes.StartedBy, audit.StartedBy);

        AddDate(item, FileMetadataAttributes.StartTimestampUtc, audit.StartTimestampUtc);
        AddNullableDate(item, FileMetadataAttributes.EndTimestampUtc, audit.EndTimestampUtc);

        AddS(item, FileMetadataAttributes.SourceSystem, audit.SourceSystem);
        AddS(item, FileMetadataAttributes.TriggerType, audit.TriggerType);
        AddS(item, FileMetadataAttributes.IngestionMode, audit.IngestionMode);

        AddN(item, FileMetadataAttributes.PayloadRecordCount, audit.PayloadRecordCount);
        AddN(item, FileMetadataAttributes.TotalRecords, audit.TotalRecords);
        AddN(item, FileMetadataAttributes.SuccessCount, audit.SuccessCount);
        AddN(item, FileMetadataAttributes.RejectCount, audit.RejectCount);
        AddN(item, FileMetadataAttributes.ValidationFailureCount, audit.ValidationFailureCount);

        AddIntMap(item, FileMetadataAttributes.FindingTypeCounts, audit.FindingTypeCounts);

        AddN(item, FileMetadataAttributes.BatchSize, audit.BatchSize);
        AddN(item, FileMetadataAttributes.TotalBatches, audit.TotalBatches);
        AddN(item, FileMetadataAttributes.PersistedBatchCount, audit.PersistedBatchCount);
        AddN(item, FileMetadataAttributes.LastSuccessfulBatchNumber, audit.LastSuccessfulBatchNumber);
        AddN(item, FileMetadataAttributes.LastProcessedRecordCount, audit.LastProcessedRecordCount);

        AddBool(item, FileMetadataAttributes.CheckpointingEnabled, audit.CheckpointingEnabled);

        AddN(item, FileMetadataAttributes.BatchPersistenceRetryCount, audit.BatchPersistenceRetryCount);
        AddN(item, FileMetadataAttributes.MaxBatchPersistenceRetryCount, audit.MaxBatchPersistenceRetryCount);

        AddS(item, FileMetadataAttributes.Status, audit.Status.ToString());

        AddS(item, FileMetadataAttributes.ErrorMessage, audit.ErrorMessage);
        AddS(item, FileMetadataAttributes.FailureReason, audit.FailureReason);
        AddS(item, FileMetadataAttributes.ArchivedFilePath, audit.ArchivedFilePath);
        AddS(item, FileMetadataAttributes.ProcessingSummaryPath, audit.ProcessingSummaryPath);

        AddBool(item, FileMetadataAttributes.IsResumeEligible, audit.IsResumeEligible);
        AddNullableDate(item, FileMetadataAttributes.LastCheckpointUtc, audit.LastCheckpointUtc);
        AddS(item, FileMetadataAttributes.CheckpointMessage, audit.CheckpointMessage);

        AddS(item, FileMetadataAttributes.WorkingFileFormat, audit.WorkingFileFormat);
        AddS(item, FileMetadataAttributes.WorkingFilePath, audit.WorkingFilePath);
        AddN(item, FileMetadataAttributes.WorkingFileRecordCount, audit.WorkingFileRecordCount);

        return item;
    }

    public static IngestionJobAudit FromIngestionJobAuditItem(Dictionary<string, AttributeValue> item)
    {
        var statusRaw = GetS(item, FileMetadataAttributes.Status);
        Enum.TryParse<IngestionJobStatus>(statusRaw, ignoreCase: true, out var parsedStatus);

        return new IngestionJobAudit
        {
            JobId = GetSOrEmpty(item, FileMetadataAttributes.JobId),
            InboundFileName = GetSOrEmpty(item, FileMetadataAttributes.InboundFileName),
            UserName = GetSOrEmpty(item, FileMetadataAttributes.UserName),
            StartedBy = GetSOrEmpty(item, FileMetadataAttributes.StartedBy),

            StartTimestampUtc = GetDateOrDefault(item, FileMetadataAttributes.StartTimestampUtc),
            EndTimestampUtc = GetNullableDate(item, FileMetadataAttributes.EndTimestampUtc),

            SourceSystem = GetS(item, FileMetadataAttributes.SourceSystem),
            TriggerType = GetSOrEmpty(item, FileMetadataAttributes.TriggerType),
            IngestionMode = GetSOrEmpty(item, FileMetadataAttributes.IngestionMode),

            PayloadRecordCount = GetIntOrZero(item, FileMetadataAttributes.PayloadRecordCount),
            TotalRecords = GetIntOrZero(item, FileMetadataAttributes.TotalRecords),
            SuccessCount = GetIntOrZero(item, FileMetadataAttributes.SuccessCount),
            RejectCount = GetIntOrZero(item, FileMetadataAttributes.RejectCount),
            ValidationFailureCount = GetIntOrZero(item, FileMetadataAttributes.ValidationFailureCount),

            FindingTypeCounts = GetIntMap(item, FileMetadataAttributes.FindingTypeCounts),

            BatchSize = GetIntOrZero(item, FileMetadataAttributes.BatchSize),
            TotalBatches = GetIntOrZero(item, FileMetadataAttributes.TotalBatches),
            PersistedBatchCount = GetIntOrZero(item, FileMetadataAttributes.PersistedBatchCount),
            LastSuccessfulBatchNumber = GetIntOrZero(item, FileMetadataAttributes.LastSuccessfulBatchNumber),
            LastProcessedRecordCount = GetIntOrZero(item, FileMetadataAttributes.LastProcessedRecordCount),

            CheckpointingEnabled = GetBoolOrFalse(item, FileMetadataAttributes.CheckpointingEnabled),

            BatchPersistenceRetryCount = GetIntOrZero(item, FileMetadataAttributes.BatchPersistenceRetryCount),
            MaxBatchPersistenceRetryCount = GetIntOrZero(item, FileMetadataAttributes.MaxBatchPersistenceRetryCount),

            Status = parsedStatus,

            ErrorMessage = GetS(item, FileMetadataAttributes.ErrorMessage),
            FailureReason = GetS(item, FileMetadataAttributes.FailureReason),
            ArchivedFilePath = GetS(item, FileMetadataAttributes.ArchivedFilePath),
            ProcessingSummaryPath = GetS(item, FileMetadataAttributes.ProcessingSummaryPath),

            IsResumeEligible = GetBoolOrFalse(item, FileMetadataAttributes.IsResumeEligible),
            LastCheckpointUtc = GetNullableDate(item, FileMetadataAttributes.LastCheckpointUtc),
            CheckpointMessage = GetS(item, FileMetadataAttributes.CheckpointMessage),

            WorkingFileFormat = GetS(item, FileMetadataAttributes.WorkingFileFormat),
            WorkingFilePath = GetS(item, FileMetadataAttributes.WorkingFilePath),
            WorkingFileRecordCount = GetIntOrZero(item, FileMetadataAttributes.WorkingFileRecordCount)
        };
    }

    // =====================================================================
    // RejectedRowDetail  <->  gfr-rejected-rows-dev
    // =====================================================================

    public static class RejectedRowAttributes
    {
        public const string RejectedRowId = "rejectedRowId";
        public const string JobId = "jobId";
        public const string InboundFileName = "inboundFileName";
        public const string SourceRecordId = "sourceRecordId";
        public const string FindingFileName = "findingFileName";
        public const string FindingType = "findingType";
        public const string UserName = "userName";
        public const string RowNumber = "rowNumber";
        public const string FieldName = "fieldName";
        public const string RejectedValue = "rejectedValue";
        public const string ErrorReason = "errorReason";
        public const string ErrorDateUtc = "errorDateUtc";
        public const string RawRowJson = "rawRowJson";
    }

    public static Dictionary<string, AttributeValue> ToItem(RejectedRowDetail row)
    {
        var item = new Dictionary<string, AttributeValue>();

        AddS(item, RejectedRowAttributes.RejectedRowId, row.RejectedRowId);
        AddS(item, RejectedRowAttributes.JobId, row.JobId);
        AddS(item, RejectedRowAttributes.InboundFileName, row.InboundFileName);
        AddS(item, RejectedRowAttributes.SourceRecordId, row.SourceRecordId);
        AddS(item, RejectedRowAttributes.FindingFileName, row.FindingFileName);
        AddS(item, RejectedRowAttributes.FindingType, row.FindingType);
        AddS(item, RejectedRowAttributes.UserName, row.UserName);
        AddN(item, RejectedRowAttributes.RowNumber, row.RowNumber);
        AddS(item, RejectedRowAttributes.FieldName, row.FieldName);
        AddS(item, RejectedRowAttributes.RejectedValue, row.RejectedValue);
        AddS(item, RejectedRowAttributes.ErrorReason, row.ErrorReason);
        AddDate(item, RejectedRowAttributes.ErrorDateUtc, row.ErrorDateUtc);
        AddS(item, RejectedRowAttributes.RawRowJson, row.RawRowJson);

        return item;
    }

    public static RejectedRowDetail FromRejectedRowItem(Dictionary<string, AttributeValue> item)
    {
        return new RejectedRowDetail
        {
            RejectedRowId = GetSOrEmpty(item, RejectedRowAttributes.RejectedRowId),
            JobId = GetSOrEmpty(item, RejectedRowAttributes.JobId),
            InboundFileName = GetSOrEmpty(item, RejectedRowAttributes.InboundFileName),
            SourceRecordId = GetS(item, RejectedRowAttributes.SourceRecordId),
            FindingFileName = GetS(item, RejectedRowAttributes.FindingFileName),
            FindingType = GetS(item, RejectedRowAttributes.FindingType),
            UserName = GetS(item, RejectedRowAttributes.UserName),
            RowNumber = GetIntOrZero(item, RejectedRowAttributes.RowNumber),
            FieldName = GetSOrEmpty(item, RejectedRowAttributes.FieldName),
            RejectedValue = GetS(item, RejectedRowAttributes.RejectedValue),
            ErrorReason = GetSOrEmpty(item, RejectedRowAttributes.ErrorReason),
            ErrorDateUtc = GetDateOrDefault(item, RejectedRowAttributes.ErrorDateUtc),
            RawRowJson = GetS(item, RejectedRowAttributes.RawRowJson)
        };
    }

    // =====================================================================
    // IngestionCheckpoint  <->  gfr-ingestion-checkpoints-dev
    // =====================================================================

    public static class CheckpointAttributes
    {
        public const string JobId = "jobId";
        public const string InboundFileName = "inboundFileName";
        public const string UserName = "userName";
        public const string SourceSystem = "sourceSystem";
        public const string TriggerType = "triggerType";
        public const string IngestionMode = "ingestionMode";
        public const string BatchSize = "batchSize";
        public const string TotalBatches = "totalBatches";
        public const string LastSuccessfulBatchNumber = "lastSuccessfulBatchNumber";
        public const string LastProcessedRecordCount = "lastProcessedRecordCount";
        public const string PersistedBatchCount = "persistedBatchCount";
        public const string SuccessCount = "successCount";
        public const string RejectCount = "rejectCount";
        public const string BatchPersistenceRetryCount = "batchPersistenceRetryCount";
        public const string Status = "status";
        public const string IsResumeEligible = "isResumeEligible";
        public const string CreatedAtUtc = "createdAtUtc";
        public const string LastCheckpointUtc = "lastCheckpointUtc";
        public const string FailureReason = "failureReason";
    }

    public static Dictionary<string, AttributeValue> ToItem(IngestionCheckpoint checkpoint)
    {
        var item = new Dictionary<string, AttributeValue>();

        AddS(item, CheckpointAttributes.JobId, checkpoint.JobId);
        AddS(item, CheckpointAttributes.InboundFileName, checkpoint.InboundFileName);
        AddS(item, CheckpointAttributes.UserName, checkpoint.UserName);
        AddS(item, CheckpointAttributes.SourceSystem, checkpoint.SourceSystem);
        AddS(item, CheckpointAttributes.TriggerType, checkpoint.TriggerType);
        AddS(item, CheckpointAttributes.IngestionMode, checkpoint.IngestionMode);

        AddN(item, CheckpointAttributes.BatchSize, checkpoint.BatchSize);
        AddN(item, CheckpointAttributes.TotalBatches, checkpoint.TotalBatches);
        AddN(item, CheckpointAttributes.LastSuccessfulBatchNumber, checkpoint.LastSuccessfulBatchNumber);
        AddN(item, CheckpointAttributes.LastProcessedRecordCount, checkpoint.LastProcessedRecordCount);
        AddN(item, CheckpointAttributes.PersistedBatchCount, checkpoint.PersistedBatchCount);
        AddN(item, CheckpointAttributes.SuccessCount, checkpoint.SuccessCount);
        AddN(item, CheckpointAttributes.RejectCount, checkpoint.RejectCount);
        AddN(item, CheckpointAttributes.BatchPersistenceRetryCount, checkpoint.BatchPersistenceRetryCount);

        AddS(item, CheckpointAttributes.Status, checkpoint.Status.ToString());
        AddBool(item, CheckpointAttributes.IsResumeEligible, checkpoint.IsResumeEligible);

        AddDate(item, CheckpointAttributes.CreatedAtUtc, checkpoint.CreatedAtUtc);
        AddDate(item, CheckpointAttributes.LastCheckpointUtc, checkpoint.LastCheckpointUtc);

        AddS(item, CheckpointAttributes.FailureReason, checkpoint.FailureReason);

        return item;
    }

    public static IngestionCheckpoint FromIngestionCheckpointItem(Dictionary<string, AttributeValue> item)
    {
        var statusRaw = GetS(item, CheckpointAttributes.Status);
        Enum.TryParse<IngestionJobStatus>(statusRaw, ignoreCase: true, out var parsedStatus);

        return new IngestionCheckpoint
        {
            JobId = GetSOrEmpty(item, CheckpointAttributes.JobId),
            InboundFileName = GetSOrEmpty(item, CheckpointAttributes.InboundFileName),
            UserName = GetSOrEmpty(item, CheckpointAttributes.UserName),
            SourceSystem = GetS(item, CheckpointAttributes.SourceSystem),
            TriggerType = GetSOrEmpty(item, CheckpointAttributes.TriggerType),
            IngestionMode = GetSOrEmpty(item, CheckpointAttributes.IngestionMode),

            BatchSize = GetIntOrZero(item, CheckpointAttributes.BatchSize),
            TotalBatches = GetIntOrZero(item, CheckpointAttributes.TotalBatches),
            LastSuccessfulBatchNumber = GetIntOrZero(item, CheckpointAttributes.LastSuccessfulBatchNumber),
            LastProcessedRecordCount = GetIntOrZero(item, CheckpointAttributes.LastProcessedRecordCount),
            PersistedBatchCount = GetIntOrZero(item, CheckpointAttributes.PersistedBatchCount),
            SuccessCount = GetIntOrZero(item, CheckpointAttributes.SuccessCount),
            RejectCount = GetIntOrZero(item, CheckpointAttributes.RejectCount),
            BatchPersistenceRetryCount = GetIntOrZero(item, CheckpointAttributes.BatchPersistenceRetryCount),

            Status = parsedStatus,
            IsResumeEligible = GetBoolOrFalse(item, CheckpointAttributes.IsResumeEligible),

            CreatedAtUtc = GetDateOrDefault(item, CheckpointAttributes.CreatedAtUtc),
            LastCheckpointUtc = GetDateOrDefault(item, CheckpointAttributes.LastCheckpointUtc),

            FailureReason = GetS(item, CheckpointAttributes.FailureReason)
        };
    }

    // =====================================================================
    // Alias methods — compatibility with existing repository call sites
    // that use ToMap(...) / ToFileFinding(...) / ToIngestionJobAudit(...) /
    // ToRejectedRowDetail(...) / ToIngestionCheckpoint(...) naming.
    // =====================================================================

    public static Dictionary<string, AttributeValue> ToMap(FileFinding finding) => ToItem(finding);
    public static Dictionary<string, AttributeValue> ToMap(IngestionJobAudit audit) => ToItem(audit);
    public static Dictionary<string, AttributeValue> ToMap(RejectedRowDetail row) => ToItem(row);
    public static Dictionary<string, AttributeValue> ToMap(IngestionCheckpoint checkpoint) => ToItem(checkpoint);

    public static FileFinding ToFileFinding(Dictionary<string, AttributeValue> item) => FromFileFindingItem(item);
    public static IngestionJobAudit ToIngestionJobAudit(Dictionary<string, AttributeValue> item) => FromIngestionJobAuditItem(item);
    public static RejectedRowDetail ToRejectedRowDetail(Dictionary<string, AttributeValue> item) => FromRejectedRowItem(item);
    public static IngestionCheckpoint ToIngestionCheckpoint(Dictionary<string, AttributeValue> item) => FromIngestionCheckpointItem(item);
}