using Amazon.DynamoDBv2.Model;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;    // IngestionJobStatus
using RemediationTool.Domain.Enums;   // FindingType, ErrorCategory

namespace RemediationTool.Infrastructure.DynamoDB;

/// <summary>
/// Single source of truth for converting domain entities to/from
/// DynamoDB AttributeValue dictionaries.
/// </summary>
public static class DynamoDbAttributeMap
{
    // -------------------------------------------------------------------------
    // FileFinding
    // -------------------------------------------------------------------------

    public static Dictionary<string, AttributeValue> ToMap(FileFinding f) => new()
    {
        ["Id"] = new AttributeValue { S = f.Id.ToString() },
        ["RecordVersionId"] = new AttributeValue { S = f.RecordVersionId ?? string.Empty },
        ["SourceRecordId"] = new AttributeValue { S = string.IsNullOrWhiteSpace(f.SourceRecordId) ? f.Id.ToString() : f.SourceRecordId},
        ["IngestionJobId"] = new AttributeValue { S = f.IngestionJobId ?? string.Empty },
        ["InboundFileName"] = new AttributeValue { S = f.InboundFileName ?? string.Empty },
        ["UserName"] = new AttributeValue { S = f.UserName ?? "System" },
        ["LoadDateUtc"] = new AttributeValue { S = f.LoadDateUtc.ToUniversalTime().ToString("O") },
        ["LastUpdateDateUtc"] = new AttributeValue { S = f.LastUpdateDateUtc.ToUniversalTime().ToString("O") },
        ["FindingFileName"] = new AttributeValue { S = f.FindingFileName ?? string.Empty },
        ["FindingFileFormat"] = new AttributeValue { S = f.FindingFileFormat ?? string.Empty },
        ["FindingFileSizeBytes"] = NNullable(f.FindingFileSizeBytes),
        ["CurrentFileLocation"] = new AttributeValue { S = f.CurrentFileLocation ?? string.Empty },
        ["FindingType"] = new AttributeValue
        {
            S = string.IsNullOrWhiteSpace(f.FindingType)
                      ? "Unknown" : f.FindingType
        },
        ["DataSystem"] = new AttributeValue
        {
            S = string.IsNullOrWhiteSpace(f.DataSystem)
                     ? "Unknown" : f.DataSystem
        },
        ["OriginatingDataSystem"] = new AttributeValue { S = f.OriginatingDataSystem ?? string.Empty },
        ["OriginatingVendorTool"] = new AttributeValue { S = f.OriginatingVendorTool ?? string.Empty },
        ["OriginalFileLocation"] = SNullable(f.OriginalFileLocation),
        ["QuarantineDateUtc"] = SNullable(f.QuarantineDateUtc),
        ["RestorationDateUtc"] = SNullable(f.RestorationDateUtc),
        ["ExceptionDateUtc"] = SNullable(f.ExceptionDateUtc),
        ["DeletionDateUtc"] = SNullable(f.DeletionDateUtc),
        ["SiteOwner"] = SNullable(f.SiteOwner),
        ["FileOwner"] = SNullable(f.FileOwner),
        ["RestorationTicketIdentifier"] = SNullable(f.RestorationTicketIdentifier),
        ["RestorationRequestorEmail"] = SNullable(f.RestorationRequestorEmail),
        ["RestorationComment"] = SNullable(f.RestorationComment),
        ["ErrorCategory"] = S(f.ErrorCategory.ToString()),  // enum — use .ToString()
        ["ErrorDetail"] = SNullable(f.ErrorDetail),
        // IsValid and IngestionErrorReason are pipeline-only — NOT persisted to DynamoDB
    };

    public static FileFinding ToFileFinding(Dictionary<string, AttributeValue> m) => new()
    {
        Id = GuidVal(m, "Id"),
        RecordVersionId = StringVal(m, "RecordVersionId"),
        SourceRecordId = NullableStringVal(m, "SourceRecordId"),
        IngestionJobId = NullableStringVal(m, "IngestionJobId"),
        InboundFileName = StringVal(m, "InboundFileName"),
        UserName = StringVal(m, "UserName"),
        LoadDateUtc = DateVal(m, "LoadDateUtc"),
        LastUpdateDateUtc = DateVal(m, "LastUpdateDateUtc"),
        FindingFileName = StringVal(m, "FindingFileName"),
        FindingFileFormat = StringVal(m, "FindingFileFormat"),
        FindingFileSizeBytes = NullableLongVal(m, "FindingFileSizeBytes"),
        CurrentFileLocation = StringVal(m, "CurrentFileLocation"),
        FindingType = StringVal(m, "FindingType"),    // string on entity
        DataSystem = StringVal(m, "DataSystem"),
        OriginatingDataSystem = StringVal(m, "OriginatingDataSystem"),
        OriginatingVendorTool = StringVal(m, "OriginatingVendorTool"),
        OriginalFileLocation = NullableStringVal(m, "OriginalFileLocation"),
        QuarantineDateUtc = NullableDateVal(m, "QuarantineDateUtc"),
        RestorationDateUtc = NullableDateVal(m, "RestorationDateUtc"),
        ExceptionDateUtc = NullableDateVal(m, "ExceptionDateUtc"),
        DeletionDateUtc = NullableDateVal(m, "DeletionDateUtc"),
        SiteOwner = NullableStringVal(m, "SiteOwner"),
        FileOwner = NullableStringVal(m, "FileOwner"),
        RestorationTicketIdentifier = NullableStringVal(m, "RestorationTicketIdentifier"),
        RestorationRequestorEmail = NullableStringVal(m, "RestorationRequestorEmail"),
        RestorationComment = NullableStringVal(m, "RestorationComment"),
        ErrorCategory = EnumVal<ErrorCategory>(m, "ErrorCategory"),
        ErrorDetail = NullableStringVal(m, "ErrorDetail"),
        // IsValid defaults to true; IngestionErrorReason defaults to empty — not stored in DynamoDB
    };

    // -------------------------------------------------------------------------
    // IngestionJobAudit
    // -------------------------------------------------------------------------

    public static Dictionary<string, AttributeValue> ToMap(IngestionJobAudit a)
    {
        var map = new Dictionary<string, AttributeValue>
        {
            ["JobId"] = S(a.JobId),
            ["InboundFileName"] = S(a.InboundFileName),
            ["UserName"] = S(a.UserName),
            ["StartedBy"] = S(a.StartedBy),
            ["StartTimestampUtc"] = S(a.StartTimestampUtc),
            ["EndTimestampUtc"] = SNullable(a.EndTimestampUtc),
            ["SourceSystem"] = SNullable(a.SourceSystem),
            ["TriggerType"] = S(a.TriggerType),
            ["IngestionMode"] = S(a.IngestionMode),
            ["PayloadRecordCount"] = N(a.PayloadRecordCount),
            ["TotalRecords"] = N(a.TotalRecords),
            ["SuccessCount"] = N(a.SuccessCount),
            ["RejectCount"] = N(a.RejectCount),
            ["ValidationFailureCount"] = N(a.ValidationFailureCount),
            ["BatchSize"] = N(a.BatchSize),
            ["TotalBatches"] = N(a.TotalBatches),
            ["PersistedBatchCount"] = N(a.PersistedBatchCount),
            ["LastSuccessfulBatchNumber"] = N(a.LastSuccessfulBatchNumber),
            ["LastProcessedRecordCount"] = N(a.LastProcessedRecordCount),
            ["CheckpointingEnabled"] = Bool(a.CheckpointingEnabled),
            ["BatchPersistenceRetryCount"] = N(a.BatchPersistenceRetryCount),
            ["MaxBatchPersistenceRetryCount"] = N(a.MaxBatchPersistenceRetryCount),
            ["Status"] = S(a.Status.ToString()),
            ["ErrorMessage"] = SNullable(a.ErrorMessage),
            ["FailureReason"] = SNullable(a.FailureReason),
            ["ArchivedFilePath"] = SNullable(a.ArchivedFilePath),
            ["ProcessingSummaryPath"] = SNullable(a.ProcessingSummaryPath),
            ["IsResumeEligible"] = Bool(a.IsResumeEligible),
            ["LastCheckpointUtc"] = SNullable(a.LastCheckpointUtc),
            ["CheckpointMessage"] = SNullable(a.CheckpointMessage),
            ["WorkingFileFormat"] = SNullable(a.WorkingFileFormat),
            ["WorkingFilePath"] = SNullable(a.WorkingFilePath),
            ["WorkingFileRecordCount"] = N(a.WorkingFileRecordCount)
        };

        // FindingTypeCounts stored as DynamoDB Map { "Obsolete": { "N": "120" }, ... }
        if (a.FindingTypeCounts != null && a.FindingTypeCounts.Count > 0)
        {
            map["FindingTypeCounts"] = new AttributeValue
            {
                M = a.FindingTypeCounts.ToDictionary(
                    kv => kv.Key,
                    kv => new AttributeValue { N = kv.Value.ToString() })
            };
        }

        return map;
    }

    public static IngestionJobAudit ToIngestionJobAudit(Dictionary<string, AttributeValue> m)
    {
        var audit = new IngestionJobAudit
        {
            JobId = StringVal(m, "JobId"),
            InboundFileName = StringVal(m, "InboundFileName"),
            UserName = StringVal(m, "UserName"),
            StartedBy = StringVal(m, "StartedBy"),
            StartTimestampUtc = DateVal(m, "StartTimestampUtc"),
            EndTimestampUtc = NullableDateVal(m, "EndTimestampUtc"),
            SourceSystem = NullableStringVal(m, "SourceSystem"),
            TriggerType = StringVal(m, "TriggerType"),
            IngestionMode = StringVal(m, "IngestionMode"),
            PayloadRecordCount = IntVal(m, "PayloadRecordCount"),
            TotalRecords = IntVal(m, "TotalRecords"),
            SuccessCount = IntVal(m, "SuccessCount"),
            RejectCount = IntVal(m, "RejectCount"),
            ValidationFailureCount = IntVal(m, "ValidationFailureCount"),
            BatchSize = IntVal(m, "BatchSize"),
            TotalBatches = IntVal(m, "TotalBatches"),
            PersistedBatchCount = IntVal(m, "PersistedBatchCount"),
            LastSuccessfulBatchNumber = IntVal(m, "LastSuccessfulBatchNumber"),
            LastProcessedRecordCount = IntVal(m, "LastProcessedRecordCount"),
            CheckpointingEnabled = BoolVal(m, "CheckpointingEnabled"),
            BatchPersistenceRetryCount = IntVal(m, "BatchPersistenceRetryCount"),
            MaxBatchPersistenceRetryCount = IntVal(m, "MaxBatchPersistenceRetryCount"),
            Status = EnumVal<IngestionJobStatus>(m, "Status"),
            ErrorMessage = NullableStringVal(m, "ErrorMessage"),
            FailureReason = NullableStringVal(m, "FailureReason"),
            ArchivedFilePath = NullableStringVal(m, "ArchivedFilePath"),
            ProcessingSummaryPath = NullableStringVal(m, "ProcessingSummaryPath"),
            IsResumeEligible = BoolVal(m, "IsResumeEligible"),
            LastCheckpointUtc = NullableDateVal(m, "LastCheckpointUtc"),
            CheckpointMessage = NullableStringVal(m, "CheckpointMessage"),
            WorkingFileFormat = NullableStringVal(m, "WorkingFileFormat"),
            WorkingFilePath = NullableStringVal(m, "WorkingFilePath"),
            WorkingFileRecordCount = IntVal(m, "WorkingFileRecordCount")
        };

        if (m.TryGetValue("FindingTypeCounts", out var ftc) && ftc.M != null)
            audit.FindingTypeCounts = ftc.M.ToDictionary(
                kv => kv.Key,
                kv => int.TryParse(kv.Value.N, out var n) ? n : 0);

        return audit;
    }

    // -------------------------------------------------------------------------
    // RejectedRowDetail
    // -------------------------------------------------------------------------

    public static Dictionary<string, AttributeValue> ToMap(RejectedRowDetail r) => new()
    {
        ["RejectedRowId"] = S(r.RejectedRowId),
        ["JobId"] = S(r.JobId),
        ["InboundFileName"] = S(r.InboundFileName),
        ["SourceRecordId"] = SNullable(r.SourceRecordId),
        ["FindingFileName"] = SNullable(r.FindingFileName),
        ["FindingType"] = SNullable(r.FindingType),
        ["UserName"] = S(r.UserName),
        ["RowNumber"] = N(r.RowNumber),
        ["FieldName"] = S(r.FieldName),
        ["RejectedValue"] = SNullable(r.RejectedValue),
        ["ErrorReason"] = S(r.ErrorReason),
        ["ErrorDateUtc"] = S(r.ErrorDateUtc),
        ["RawRowJson"] = SNullable(r.RawRowJson)
    };

    public static RejectedRowDetail ToRejectedRowDetail(Dictionary<string, AttributeValue> m) => new()
    {
        RejectedRowId = StringVal(m, "RejectedRowId"),
        JobId = StringVal(m, "JobId"),
        InboundFileName = StringVal(m, "InboundFileName"),
        SourceRecordId = NullableStringVal(m, "SourceRecordId"),
        FindingFileName = NullableStringVal(m, "FindingFileName"),
        FindingType = NullableStringVal(m, "FindingType"),
        UserName = StringVal(m, "UserName"),
        RowNumber = IntVal(m, "RowNumber"),
        FieldName = StringVal(m, "FieldName"),
        RejectedValue = NullableStringVal(m, "RejectedValue"),
        ErrorReason = StringVal(m, "ErrorReason"),
        ErrorDateUtc = DateVal(m, "ErrorDateUtc"),
        RawRowJson = NullableStringVal(m, "RawRowJson")
    };

    // -------------------------------------------------------------------------
    // IngestionCheckpoint
    // -------------------------------------------------------------------------

    public static Dictionary<string, AttributeValue> ToMap(IngestionCheckpoint c) => new()
    {
        ["JobId"] = S(c.JobId),
        ["InboundFileName"] = S(c.InboundFileName),
        ["UserName"] = S(c.UserName),
        ["SourceSystem"] = SNullable(c.SourceSystem),
        ["TriggerType"] = S(c.TriggerType),
        ["IngestionMode"] = S(c.IngestionMode),
        ["BatchSize"] = N(c.BatchSize),
        ["TotalBatches"] = N(c.TotalBatches),
        ["LastSuccessfulBatchNumber"] = N(c.LastSuccessfulBatchNumber),
        ["LastProcessedRecordCount"] = N(c.LastProcessedRecordCount),
        ["PersistedBatchCount"] = N(c.PersistedBatchCount),
        ["SuccessCount"] = N(c.SuccessCount),
        ["RejectCount"] = N(c.RejectCount),
        ["BatchPersistenceRetryCount"] = N(c.BatchPersistenceRetryCount),
        ["Status"] = S(c.Status.ToString()),
        ["IsResumeEligible"] = Bool(c.IsResumeEligible),
        ["CreatedAtUtc"] = S(c.CreatedAtUtc),
        ["LastCheckpointUtc"] = S(c.LastCheckpointUtc),
        ["FailureReason"] = SNullable(c.FailureReason),
        ["WorkingFilePath"] = SNullable(c.WorkingFilePath),
        ["WorkingFileFormat"] = SNullable(c.WorkingFileFormat),
        ["WorkingFileRecordCount"] = N(c.WorkingFileRecordCount)
    };

    public static IngestionCheckpoint ToIngestionCheckpoint(Dictionary<string, AttributeValue> m) => new()
    {
        JobId = StringVal(m, "JobId"),
        InboundFileName = StringVal(m, "InboundFileName"),
        UserName = StringVal(m, "UserName"),
        SourceSystem = NullableStringVal(m, "SourceSystem"),
        TriggerType = StringVal(m, "TriggerType"),
        IngestionMode = StringVal(m, "IngestionMode"),
        BatchSize = IntVal(m, "BatchSize"),
        TotalBatches = IntVal(m, "TotalBatches"),
        LastSuccessfulBatchNumber = IntVal(m, "LastSuccessfulBatchNumber"),
        LastProcessedRecordCount = IntVal(m, "LastProcessedRecordCount"),
        PersistedBatchCount = IntVal(m, "PersistedBatchCount"),
        SuccessCount = IntVal(m, "SuccessCount"),
        RejectCount = IntVal(m, "RejectCount"),
        BatchPersistenceRetryCount = IntVal(m, "BatchPersistenceRetryCount"),
        Status = EnumVal<IngestionJobStatus>(m, "Status"),
        IsResumeEligible = BoolVal(m, "IsResumeEligible"),
        CreatedAtUtc = DateVal(m, "CreatedAtUtc"),
        LastCheckpointUtc = DateVal(m, "LastCheckpointUtc"),
        FailureReason = NullableStringVal(m, "FailureReason"),
        WorkingFilePath = NullableStringVal(m, "WorkingFilePath"),
        WorkingFileFormat = NullableStringVal(m, "WorkingFileFormat"),
        WorkingFileRecordCount = IntVal(m, "WorkingFileRecordCount")
    };

    // -------------------------------------------------------------------------
    // Low-level attribute builders
    // -------------------------------------------------------------------------

    private static AttributeValue S(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new AttributeValue { NULL = true }
            : new AttributeValue { S = value };

    private static AttributeValue S(DateTime dt) =>
        new() { S = dt.ToUniversalTime().ToString("O") };

    private static AttributeValue SNullable(string? value) =>
        value == null ? new AttributeValue { NULL = true } : S(value);

    private static AttributeValue SNullable(DateTime? dt) =>
        dt == null
            ? new AttributeValue { NULL = true }
            : new AttributeValue { S = dt.Value.ToUniversalTime().ToString("O") };

    private static AttributeValue N(int value) => new() { N = value.ToString() };
    private static AttributeValue N(long value) => new() { N = value.ToString() };

    private static AttributeValue NNullable(long? value) =>
        value == null ? new AttributeValue { NULL = true } : new AttributeValue { N = value.ToString() };

    private static AttributeValue Bool(bool value) => new() { BOOL = value };

    // -------------------------------------------------------------------------
    // Low-level attribute readers
    // -------------------------------------------------------------------------

    private static string StringVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && v.S != null ? v.S : string.Empty;

    private static string? NullableStringVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && v.S != null ? v.S : null;

    private static Guid GuidVal(Dictionary<string, AttributeValue> m, string key) =>
        Guid.TryParse(StringVal(m, key), out var g) ? g : Guid.Empty;

    private static int IntVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && int.TryParse(v.N, out var n) ? n : 0;

    private static long? NullableLongVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && v.N != null && long.TryParse(v.N, out var n) ? n : null;

    private static bool BoolVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && v.BOOL == true;

    private static DateTime DateVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && v.S != null &&
        DateTime.TryParse(v.S, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt : DateTime.MinValue;

    private static DateTime? NullableDateVal(Dictionary<string, AttributeValue> m, string key) =>
        m.TryGetValue(key, out var v) && v.S != null &&
        DateTime.TryParse(v.S, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;

    private static T EnumVal<T>(Dictionary<string, AttributeValue> m, string key)
        where T : struct, Enum
    {
        var s = StringVal(m, key);
        return Enum.TryParse<T>(s, ignoreCase: true, out var result) ? result : default;
    }
}