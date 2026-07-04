using Amazon.DynamoDBv2.Model;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;

namespace RemediationTool.Infrastructure.DynamoDB;

/// <summary>
/// Maps domain entities to/from DynamoDB attribute dictionaries.
/// All DynamoDB attribute names are camelCase per GFR naming standards.
/// FindingType is stored and read as a plain string — no enum conversion.
/// </summary>
public static class DynamoDbAttributeMap
{
    // =========================================================================
    // Helper methods
    // =========================================================================

    private static void AddS(Dictionary<string, AttributeValue> item, string key, string? value)
    {
        if (value != null)
            item[key] = new AttributeValue { S = value };
    }

    private static void AddN(Dictionary<string, AttributeValue> item, string key, long value)
        => item[key] = new AttributeValue { N = value.ToString() };

    private static void AddNullableN(Dictionary<string, AttributeValue> item, string key, long? value)
    {
        if (value.HasValue)
            item[key] = new AttributeValue { N = value.Value.ToString() };
    }

    private static void AddDate(Dictionary<string, AttributeValue> item, string key, DateTime value)
        => item[key] = new AttributeValue { S = value.ToString("o") };

    private static void AddNullableDate(Dictionary<string, AttributeValue> item, string key, DateTime? value)
    {
        if (value.HasValue)
            item[key] = new AttributeValue { S = value.Value.ToString("o") };
    }

    private static void AddBool(Dictionary<string, AttributeValue> item, string key, bool value)
        => item[key] = new AttributeValue { BOOL = value };

    private static void AddIntMap(Dictionary<string, AttributeValue> item, string key, Dictionary<string, int>? value)
    {
        if (value == null || value.Count == 0) return;
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
        => item.TryGetValue(key, out var v) && v.N != null ? long.Parse(v.N) : null;

    private static int GetIntOrZero(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.N != null ? int.Parse(v.N) : 0;

    private static long GetLongOrZero(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.N != null ? long.Parse(v.N) : 0;

    private static bool GetBoolOrFalse(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.BOOL == true;

    private static DateTime GetDateOrDefault(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.S != null
            ? DateTime.Parse(v.S).ToUniversalTime() : default;

    private static DateTime? GetNullableDate(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var v) && v.S != null
            ? DateTime.Parse(v.S).ToUniversalTime() : null;

    private static Dictionary<string, int> GetIntMap(Dictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var v) || v.M == null)
            return new Dictionary<string, int>();
        return v.M.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.N != null ? int.Parse(kvp.Value.N) : 0);
    }

    // =========================================================================
    // FileFinding  <->  gfr-file-findings-dev
    // =========================================================================

    public static Dictionary<string, AttributeValue> ToMap(FileFinding f)
    {
        var item = new Dictionary<string, AttributeValue>();

        // CONFIRMED by Deepan (2026-07-01):
        //   id  = the individual CSV row identifier (unique per finding row)
        //   uid = the parent job's id from gfr-edg-reports-dev (= reportUid / jobId)
        // So: id  → FileFinding.Id       (the row's own GUID)
        //     uid → FileFinding.IngestionJobId  (the job it belongs to)
        AddS(item, "id", f.Id.ToString());
        AddS(item, "uid", f.IngestionJobId); // job link — replaces old "jobId" attribute name

        // TYPE CHANGE: recordVersionId is now stored as a DynamoDB Number (e.g. 1, 2, 3)
        // instead of the previous 32-char hex string. The C# property itself stays
        // string (Guid.NewGuid().ToString("N") default) to avoid breaking any other
        // code that reads FileFinding.RecordVersionId as text — only the DynamoDB
        // wire format changes here, parsed/formatted at this boundary.
        if (int.TryParse(f.RecordVersionId, out var versionAsInt))
            AddN(item, "recordVersionId", versionAsInt);
        else
            AddN(item, "recordVersionId", 1); // fallback for non-numeric legacy values

        AddS(item, "sourceRecordId", f.SourceRecordId);
        // NOTE: old attribute "jobId" removed — "uid" now carries the job link
        // in gfr-edg-findings-dev (confirmed by Deepan 2026-07-01)
        AddS(item, "inboundFileName", f.InboundFileName);
        AddS(item, "userName", f.UserName);

        // RENAMED: loadDateUtc -> rowCreatedDateOn (per gfr-edg-findings-dev export)
        AddDate(item, "rowCreatedDateOn", f.LoadDateUtc);
        AddDate(item, "lastUpdateDateUtc", f.LastUpdateDateUtc);
        AddS(item, "findingFileName", f.FindingFileName);
        AddS(item, "findingFileFormat", f.FindingFileFormat);
        AddNullableN(item, "findingFileSizeBytes", f.FindingFileSizeBytes);
        AddS(item, "currentFileLocation", f.CurrentFileLocation);
        AddS(item, "findingType", f.FindingType);   // stored as string
        AddS(item, "originatingDataSystem", f.OriginatingDataSystem);
        AddS(item, "originatingVendorTool", f.OriginatingVendorTool);

        // NEW FIELD — confirmed in both findings and rejected samples
        AddS(item, "dataSystem", f.SourceSystemPlatform);
        AddS(item, "errorCategory", f.ErrorCategory ?? string.Empty);

        // RENAMED: lastModifiedDateUtc -> fileLastModifiedOn (per export)
        AddNullableDate(item, "fileLastModifiedOn", f.LastModifiedDateUtc);
        AddNullableDate(item, "createdDateUtc", f.CreatedDateUtc);
        AddNullableDate(item, "lastAccessedDateUtc", f.LastAccessedDateUtc);
        AddNullableDate(item, "detectionDateUtc", f.DetectionDateUtc);
        AddS(item, "siteOwner", f.SiteOwner);
        AddS(item, "fileOwner", f.FileOwner);
        AddS(item, "riskLevel", f.RiskLevel);
        AddS(item, "originalFileLocation", f.OriginalFileLocation);
        AddNullableDate(item, "quarantineDateUtc", f.QuarantineDateUtc);
        AddNullableDate(item, "restoredDateUtc", f.RestoredDateUtc);
        AddNullableDate(item, "deletedDateUtc", f.DeletedDateUtc);
        AddS(item, "restorationTicketIdentifier", f.RestorationTicketIdentifier);
        AddS(item, "restorationRequestorEmail", f.RestorationRequestorEmail);
        AddS(item, "restorationComment", f.RestorationComment);

        // NOTE: export sample uses "Status" (capital S) while every other
        // attribute in this table is camelCase. Writing lowercase "status"
        // here for internal consistency with the rest of this mapper — if
        // the live table genuinely requires capital-S "Status" as the
        // attribute name, change the key below to "Status" to match exactly.
        AddS(item, "status", f.Status.ToString());
        AddS(item, "errorReason", f.ErrorReason);

        return item;
    }

    public static FileFinding ToFileFinding(Dictionary<string, AttributeValue> item)
    {
        var statusRaw = GetS(item, "status") ?? GetS(item, "Status");
        Enum.TryParse<FileStatus>(statusRaw, ignoreCase: true, out var parsedStatus);

        return new FileFinding
        {
            // CONFIRMED by Deepan (2026-07-01):
            // id  = the individual row's GUID
            // uid = the parent job id from gfr-edg-reports-dev (= reportUid)
            Id = Guid.TryParse(GetS(item, "id"), out var id) ? id : Guid.NewGuid(),

            // recordVersionId now read as a Number; falls back to the legacy
            // string attribute if the Number isn't present (pre-migration rows).
            RecordVersionId = item.TryGetValue("recordVersionId", out var rv) && rv.N != null
                ? rv.N
                : GetSOrEmpty(item, "recordVersionId"),

            SourceRecordId = GetS(item, "sourceRecordId"),

            // uid is the new name for the job link (old attribute was "jobId")
            // Reads "uid" first, falls back to legacy "jobId" for pre-migration rows.
            IngestionJobId = GetS(item, "uid") ?? GetSOrEmpty(item, "jobId"),
            InboundFileName = GetSOrEmpty(item, "inboundFileName"),
            UserName = GetSOrEmpty(item, "userName"),

            // RENAMED: reads "rowCreatedDateOn" now, falls back to legacy "loadDateUtc"
            LoadDateUtc = item.ContainsKey("rowCreatedDateOn")
                ? GetDateOrDefault(item, "rowCreatedDateOn")
                : GetDateOrDefault(item, "loadDateUtc"),

            LastUpdateDateUtc = GetDateOrDefault(item, "lastUpdateDateUtc"),
            FindingFileName = GetSOrEmpty(item, "findingFileName"),
            FindingFileFormat = GetSOrEmpty(item, "findingFileFormat"),
            FindingFileSizeBytes = GetNullableLong(item, "findingFileSizeBytes"),
            CurrentFileLocation = GetSOrEmpty(item, "currentFileLocation"),
            FindingType = GetSOrEmpty(item, "findingType"),  // plain string
            OriginatingDataSystem = GetSOrEmpty(item, "originatingDataSystem"),
            OriginatingVendorTool = GetSOrEmpty(item, "originatingVendorTool"),

            SourceSystemPlatform = GetS(item, "dataSystem"),
            ErrorCategory = GetS(item, "errorCategory"),

            // RENAMED: reads "fileLastModifiedOn" now, falls back to legacy "lastModifiedDateUtc"
            LastModifiedDateUtc = item.ContainsKey("fileLastModifiedOn")
                ? GetNullableDate(item, "fileLastModifiedOn")
                : GetNullableDate(item, "lastModifiedDateUtc"),
            CreatedDateUtc = GetNullableDate(item, "createdDateUtc"),
            LastAccessedDateUtc = GetNullableDate(item, "lastAccessedDateUtc"),
            DetectionDateUtc = GetNullableDate(item, "detectionDateUtc"),
            SiteOwner = GetS(item, "siteOwner"),
            FileOwner = GetS(item, "fileOwner"),
            RiskLevel = GetS(item, "riskLevel"),
            OriginalFileLocation = GetS(item, "originalFileLocation"),
            QuarantineDateUtc = GetNullableDate(item, "quarantineDateUtc"),
            RestoredDateUtc = GetNullableDate(item, "restoredDateUtc"),
            DeletedDateUtc = GetNullableDate(item, "deletedDateUtc"),
            RestorationTicketIdentifier = GetS(item, "restorationTicketIdentifier"),
            RestorationRequestorEmail = GetS(item, "restorationRequestorEmail"),
            RestorationComment = GetS(item, "restorationComment"),
            Status = parsedStatus,
            ErrorReason = GetSOrEmpty(item, "errorReason"),
        };
    }

    // =========================================================================
    // IngestionJobAudit  <->  gfr-file-metadata-dev
    // =========================================================================

    public static Dictionary<string, AttributeValue> ToMap(IngestionJobAudit a)
    {
        var item = new Dictionary<string, AttributeValue>();

        AddS(item, "jobId", a.JobId);
        AddS(item, "uid", a.ReportUid);
        AddS(item, "inboundFileName", a.InboundFileName);

        // Sample uses "inboundFileSizeBytes" not "fileSizeBytes"
        AddN(item, "inboundFileSizeBytes", a.FileSizeBytes);
        AddS(item, "inboundFileContentType", a.InboundFileContentType);  // new field
        AddS(item, "fileFormat", a.FileFormat);
        AddS(item, "s3FolderPath", a.S3FolderPath);
        AddS(item, "s3FilePath", a.SourceFilePath);
        AddS(item, "processingSummaryPath", a.MetadataJsonPath);
        AddS(item, "workingFilePath", a.WorkingFilePath);
        AddS(item, "workingFileFormat", a.WorkingFileFormat);
        AddN(item, "workingFileRecordCount", a.WorkingFileRecordCount);
        AddS(item, "UploadedBy", a.UploadedBy);              // PascalCase — matches sample
        AddS(item, "userName", a.UserName);
        AddS(item, "startedBy", a.StartedBy);
        AddS(item, "UploadedDisplayName", a.UploadedDisplayName);
        AddS(item, "UploadedEmailId", a.UploadedEmailId);
        AddS(item, "inboundFileChecksum", a.InboundFileChecksum);
        AddDate(item, "startTimestampUtc", a.StartTimestampUtc);
        AddNullableDate(item, "endTimestampUtc", a.EndTimestampUtc);

        // Status stored as string — "Completed", "Failed", etc.
        AddS(item, "status", a.Status.ToString());
        AddS(item, "errorMessage", a.ErrorMessage);
        AddS(item, "failureReason", a.FailureReason);
        AddS(item, "sourceSystem", a.SourceSystem);
        AddS(item, "triggerType", a.TriggerType);
        AddS(item, "ingestionMode", a.IngestionMode);
        AddN(item, "payloadRecordCount", a.PayloadRecordCount);
        AddN(item, "totalRecords", a.TotalRecords);
        AddN(item, "successCount", a.SuccessCount);
        AddN(item, "rejectCount", a.RejectCount);
        AddN(item, "validationFailureCount", a.ValidationFailureCount);

        // Sample stores findingTypeCounts as a JSON string e.g. "{\"Obsolete\":10,\"Quarantined\":6}"
        // not as a DynamoDB Map — write as a serialised JSON string to match exactly
        if (a.FindingTypeCounts != null && a.FindingTypeCounts.Count > 0)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(a.FindingTypeCounts);
            AddS(item, "findingTypeCounts", json);
        }

        AddN(item, "batchSize", a.BatchSize);
        AddN(item, "totalBatches", a.TotalBatches);
        AddN(item, "persistedBatchCount", a.PersistedBatchCount);
        AddN(item, "lastSuccessfulBatchNumber", a.LastSuccessfulBatchNumber);
        AddN(item, "lastProcessedRecordCount", a.LastProcessedRecordCount);
        AddBool(item, "checkpointingEnabled", a.CheckpointingEnabled);
        AddN(item, "batchPersistenceRetryCount", a.BatchPersistenceRetryCount);
        AddN(item, "maxBatchPersistenceRetryCount", a.MaxBatchPersistenceRetryCount);
        AddBool(item, "isResumeEligible", a.IsResumeEligible);
        AddNullableDate(item, "lastCheckpointUtc", a.LastCheckpointUtc);
        AddS(item, "checkpointMessage", a.CheckpointMessage);

        return item;
    }

    public static IngestionJobAudit ToIngestionJobAudit(Dictionary<string, AttributeValue> item)
    {
        Enum.TryParse<IngestionJobStatus>(GetS(item, "status"), ignoreCase: true, out var status);

        return new IngestionJobAudit
        {
            JobId = GetSOrEmpty(item, "jobId"),
            ReportUid = GetS(item, "uid") ?? GetSOrEmpty(item, "reportUid"),
            InboundFileName = GetSOrEmpty(item, "inboundFileName"),

            // "inboundFileSizeBytes" in new table, "fileSizeBytes" in old
            FileSizeBytes = item.ContainsKey("inboundFileSizeBytes")
                ? GetLongOrZero(item, "inboundFileSizeBytes")
                : GetLongOrZero(item, "fileSizeBytes"),

            InboundFileContentType = GetS(item, "inboundFileContentType"),
            FileFormat = GetSOrEmpty(item, "fileFormat"),
            S3FolderPath = GetSOrEmpty(item, "s3FolderPath"),
            SourceFilePath = GetS(item, "s3FilePath") ?? GetSOrEmpty(item, "sourceFilePath"),
            MetadataJsonPath = GetS(item, "processingSummaryPath") ?? GetSOrEmpty(item, "metadataJsonPath"),
            WorkingFilePath = GetS(item, "workingFilePath"),
            WorkingFileFormat = GetS(item, "workingFileFormat"),
            WorkingFileRecordCount = GetIntOrZero(item, "workingFileRecordCount"),
            UploadedBy = GetS(item, "UploadedBy") ?? GetSOrEmpty(item, "uploadedBy"),
            UserName = GetSOrEmpty(item, "userName"),
            StartedBy = GetSOrEmpty(item, "startedBy"),
            UploadedDisplayName = GetS(item, "UploadedDisplayName"),
            UploadedEmailId = GetS(item, "UploadedEmailId"),
            InboundFileChecksum = GetS(item, "inboundFileChecksum"),
            StartTimestampUtc = GetDateOrDefault(item, "startTimestampUtc"),
            EndTimestampUtc = GetNullableDate(item, "endTimestampUtc"),
            Status = status,
            ErrorMessage = GetS(item, "errorMessage"),
            FailureReason = GetS(item, "failureReason"),
            SourceSystem = GetS(item, "sourceSystem"),
            TriggerType = GetSOrEmpty(item, "triggerType"),
            IngestionMode = GetSOrEmpty(item, "ingestionMode"),
            PayloadRecordCount = GetIntOrZero(item, "payloadRecordCount"),
            TotalRecords = GetIntOrZero(item, "totalRecords"),
            SuccessCount = GetIntOrZero(item, "successCount"),
            RejectCount = GetIntOrZero(item, "rejectCount"),
            ValidationFailureCount = GetIntOrZero(item, "validationFailureCount"),

            // findingTypeCounts stored as JSON string in new table, DynamoDB Map in old
            FindingTypeCounts = item.TryGetValue("findingTypeCounts", out var ftc)
                ? (ftc.S != null
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(ftc.S) ?? new()
                    : GetIntMap(item, "findingTypeCounts"))
                : new Dictionary<string, int>(),
            BatchSize = GetIntOrZero(item, "batchSize"),
            TotalBatches = GetIntOrZero(item, "totalBatches"),
            PersistedBatchCount = GetIntOrZero(item, "persistedBatchCount"),
            LastSuccessfulBatchNumber = GetIntOrZero(item, "lastSuccessfulBatchNumber"),
            LastProcessedRecordCount = GetIntOrZero(item, "lastProcessedRecordCount"),
            CheckpointingEnabled = GetBoolOrFalse(item, "checkpointingEnabled"),
            BatchPersistenceRetryCount = GetIntOrZero(item, "batchPersistenceRetryCount"),
            MaxBatchPersistenceRetryCount = GetIntOrZero(item, "maxBatchPersistenceRetryCount"),
            IsResumeEligible = GetBoolOrFalse(item, "isResumeEligible"),
            LastCheckpointUtc = GetNullableDate(item, "lastCheckpointUtc"),
            CheckpointMessage = GetS(item, "checkpointMessage"),
        };
    }

    // =========================================================================
    // RejectedRowDetail  <->  gfr-edg-rejected-dev
    // =========================================================================

    public static Dictionary<string, AttributeValue> ToMap(RejectedRowDetail r)
    {
        var item = new Dictionary<string, AttributeValue>();

        // Schema confirmed from gfr-edg-rejected-dev sample (2026-07-01)
        // id  = unique row GUID (was "rejectedRowId")
        // uid = job link / reportUid (was "jobId") — same pattern as findings table
        AddS(item, "id",  r.Id);
        AddS(item, "uid", r.Uid);

        AddS(item, "inboundFileName",       r.InboundFileName);
        AddS(item, "sourceRecordId",        r.SourceRecordId);
        AddS(item, "findingFileName",       r.FindingFileName);
        AddS(item, "findingType",           r.FindingType ?? "Error");
        AddS(item, "userName",              r.UserName);
        AddS(item, "currentFileLocation",   r.CurrentFileLocation);
        AddS(item, "dataSystem",            r.DataSystem);
        AddS(item, "fileOwner",             r.FileOwner);
        AddS(item, "siteOwner",             r.SiteOwner);
        AddS(item, "findingFileFormat",     r.FindingFileFormat);
        AddS(item, "originatingDataSystem", r.OriginatingDataSystem);
        AddS(item, "originatingVendorTool", r.OriginatingVendorTool);
        AddS(item, "quarantineDate",        r.QuarantineDate ?? string.Empty);
        AddS(item, "Status",                r.Status);     // PascalCase — matches sample

        if (r.FindingFileSizeBytes.HasValue)
            AddNullableN(item, "findingFileSizeBytes", r.FindingFileSizeBytes);

        AddN(item, "recordVersionId", r.RecordVersionId);
        AddDate(item, "rowCreatedDateOn", r.ErrorDateUtc);
        AddNullableDate(item, "fileLastModifiedOn", r.FileLastModifiedOn);

        // Infrastructure error fields (new in gfr-edg-rejected-dev)
        AddS(item, "errorCategory", r.ErrorCategory);
        AddS(item, "stackTrace",    r.StackTrace);

        // CSV validation rejection fields (kept for row-level validation errors)
        AddN(item, "rowNumber",         r.RowNumber);
        AddS(item, "fieldName",         r.FieldName);
        AddS(item, "rejectedValue",     r.RejectedValue);
        AddS(item, "errorReason",       r.ErrorReason);
        AddS(item, "rawRowJson",        r.RawRowJson);

        return item;
    }

    public static RejectedRowDetail ToRejectedRowDetail(Dictionary<string, AttributeValue> item)
    {
        return new RejectedRowDetail
        {
            Id                  = GetS(item, "id")  ?? Guid.NewGuid().ToString(),
            Uid                 = GetS(item, "uid") ?? GetSOrEmpty(item, "jobId"),  // fallback for pre-migration rows
            InboundFileName     = GetSOrEmpty(item, "inboundFileName"),
            SourceRecordId      = GetS(item, "sourceRecordId"),
            FindingFileName     = GetS(item, "findingFileName"),
            FindingType         = GetS(item, "findingType"),
            UserName            = GetS(item, "userName"),
            CurrentFileLocation = GetS(item, "currentFileLocation"),
            DataSystem          = GetS(item, "dataSystem"),
            FileOwner           = GetS(item, "fileOwner"),
            SiteOwner           = GetS(item, "siteOwner"),
            FindingFileFormat   = GetS(item, "findingFileFormat"),
            FindingFileSizeBytes = GetNullableLong(item, "findingFileSizeBytes"),
            OriginatingDataSystem = GetSOrEmpty(item, "originatingDataSystem"),
            OriginatingVendorTool = GetSOrEmpty(item, "originatingVendorTool"),
            QuarantineDate      = GetS(item, "quarantineDate"),
            Status              = GetS(item, "Status") ?? GetSOrEmpty(item, "status"),
            RecordVersionId     = GetIntOrZero(item, "recordVersionId"),
            ErrorDateUtc        = GetDateOrDefault(item, "rowCreatedDateOn"),
            FileLastModifiedOn  = GetNullableDate(item, "fileLastModifiedOn"),
            ErrorCategory       = GetS(item, "errorCategory"),
            StackTrace          = GetS(item, "stackTrace"),
            RowNumber           = GetIntOrZero(item, "rowNumber"),
            FieldName           = GetSOrEmpty(item, "fieldName"),
            RejectedValue       = GetS(item, "rejectedValue"),
            ErrorReason         = GetSOrEmpty(item, "errorReason"),
            RawRowJson          = GetS(item, "rawRowJson"),
        };
    }

    // =========================================================================
    // IngestionCheckpoint  <->  gfr-ingestion-checkpoints-dev
    // =========================================================================

    public static Dictionary<string, AttributeValue> ToMap(IngestionCheckpoint c)
    {
        var item = new Dictionary<string, AttributeValue>();

        AddS(item, "jobId", c.JobId);
        AddS(item, "inboundFileName", c.InboundFileName);
        AddS(item, "userName", c.UserName);
        AddS(item, "sourceSystem", c.SourceSystem);
        AddS(item, "triggerType", c.TriggerType);
        AddS(item, "ingestionMode", c.IngestionMode);
        AddN(item, "batchSize", c.BatchSize);
        AddN(item, "totalBatches", c.TotalBatches);
        AddN(item, "lastSuccessfulBatchNumber", c.LastSuccessfulBatchNumber);
        AddN(item, "lastProcessedRecordCount", c.LastProcessedRecordCount);
        AddN(item, "persistedBatchCount", c.PersistedBatchCount);
        AddN(item, "successCount", c.SuccessCount);
        AddN(item, "rejectCount", c.RejectCount);
        AddN(item, "batchPersistenceRetryCount", c.BatchPersistenceRetryCount);
        AddS(item, "status", c.Status.ToString());
        AddBool(item, "isResumeEligible", c.IsResumeEligible);
        AddDate(item, "createdAtUtc", c.CreatedAtUtc);
        AddDate(item, "lastCheckpointUtc", c.LastCheckpointUtc);
        AddS(item, "failureReason", c.FailureReason);
        AddS(item, "workingFilePath", c.WorkingFilePath);
        AddS(item, "workingFileFormat", c.WorkingFileFormat);
        AddN(item, "workingFileRecordCount", c.WorkingFileRecordCount);

        return item;
    }

    public static IngestionCheckpoint ToIngestionCheckpoint(Dictionary<string, AttributeValue> item)
    {
        Enum.TryParse<IngestionJobStatus>(GetS(item, "status"), ignoreCase: true, out var status);

        return new IngestionCheckpoint
        {
            JobId = GetSOrEmpty(item, "jobId"),
            InboundFileName = GetSOrEmpty(item, "inboundFileName"),
            UserName = GetSOrEmpty(item, "userName"),
            SourceSystem = GetS(item, "sourceSystem"),
            TriggerType = GetSOrEmpty(item, "triggerType"),
            IngestionMode = GetSOrEmpty(item, "ingestionMode"),
            BatchSize = GetIntOrZero(item, "batchSize"),
            TotalBatches = GetIntOrZero(item, "totalBatches"),
            LastSuccessfulBatchNumber = GetIntOrZero(item, "lastSuccessfulBatchNumber"),
            LastProcessedRecordCount = GetIntOrZero(item, "lastProcessedRecordCount"),
            PersistedBatchCount = GetIntOrZero(item, "persistedBatchCount"),
            SuccessCount = GetIntOrZero(item, "successCount"),
            RejectCount = GetIntOrZero(item, "rejectCount"),
            BatchPersistenceRetryCount = GetIntOrZero(item, "batchPersistenceRetryCount"),
            Status = status,
            IsResumeEligible = GetBoolOrFalse(item, "isResumeEligible"),
            CreatedAtUtc = GetDateOrDefault(item, "createdAtUtc"),
            LastCheckpointUtc = GetDateOrDefault(item, "lastCheckpointUtc"),
            FailureReason = GetS(item, "failureReason"),
            WorkingFilePath = GetS(item, "workingFilePath"),
            WorkingFileFormat = GetS(item, "workingFileFormat"),
            WorkingFileRecordCount = GetIntOrZero(item, "workingFileRecordCount"),
        };
    }

}