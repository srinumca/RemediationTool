using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Data;
using Parquet.File;
using Parquet.Schema;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Strategies;

public class ParquetIngestionWorkingFileStrategy : IIngestionWorkingFileStrategy
{
    private readonly IStorageService _storage;
    private readonly IngestionProcessingOptions _options;
    private readonly ILogger<ParquetIngestionWorkingFileStrategy> _logger;

    public ParquetIngestionWorkingFileStrategy(
        IStorageService storage,
        IOptions<IngestionProcessingOptions> options,
        ILogger<ParquetIngestionWorkingFileStrategy> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public string Format => "Parquet";

    public async Task<IngestionWorkingFileResult> WriteAsync(
        string jobId,
        string inboundFileName,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("JobId is required.", nameof(jobId));
        ArgumentNullException.ThrowIfNull(validFindings);

        var workingFilePath = IngestionWorkingFilePathBuilder.BuildParquetPath(jobId, inboundFileName, DateTime.UtcNow);
        var rowGroupSize = Math.Max(1, _options.ParquetRowGroupSize);
        var fields = CreateFields();
        var schema = new ParquetSchema(fields.All);

        _logger.LogInformation("[PARQUET_WRITE_START] JobId:{JobId}, Path:{Path}, Records:{Records}, RowGroupSize:{RowGroupSize}",
            jobId, workingFilePath, validFindings.Count, rowGroupSize);

        await using var parquetStream = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, parquetStream, cancellationToken: cancellationToken))
        {
            var rowGroupNumber = 0;
            foreach (var rows in validFindings.Chunk(rowGroupSize).Select(x => x.ToList()))
            {
                rowGroupNumber++;
                using var rowGroupWriter = writer.CreateRowGroup();
                await WriteRowsAsync(rowGroupWriter, fields, rows, cancellationToken);
                _logger.LogInformation("[PARQUET_ROW_GROUP_WRITTEN] JobId:{JobId}, RowGroup:{RowGroup}, Records:{Records}",
                    jobId, rowGroupNumber, rows.Count);
            }
        }

        parquetStream.Position = 0;
        await _storage.UploadAsync(workingFilePath, parquetStream);

        _logger.LogInformation("[PARQUET_WRITE_COMPLETE] JobId:{JobId}, Path:{Path}, Records:{Records}",
            jobId, workingFilePath, validFindings.Count);

        return new IngestionWorkingFileResult
        {
            Format = Format,
            Path = workingFilePath,
            RecordCount = validFindings.Count
        };
    }

    public async Task<List<FileFinding>> ReadAfterAsync(
        string workingFilePath,
        int lastProcessedRecordCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingFilePath)) throw new ArgumentException("Working file path is required.", nameof(workingFilePath));
        if (lastProcessedRecordCount < 0) throw new ArgumentOutOfRangeException(nameof(lastProcessedRecordCount));

        _logger.LogInformation("[PARQUET_RESUME_READ_START] Path:{Path}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            workingFilePath, lastProcessedRecordCount);

        await using var parquetStream = await _storage.DownloadAsync(workingFilePath);
        if (parquetStream.CanSeek) parquetStream.Position = 0;

        using var reader = await ParquetReader.CreateAsync(parquetStream, cancellationToken: cancellationToken);
        var dataFields = reader.Schema.GetDataFields().ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var output = new List<FileFinding>();
        var globalIndex = 0;

        for (var groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(groupIndex);
            var idColumn = await ReadColumnAsync(rowGroupReader, dataFields, "Id", cancellationToken);
            if (idColumn?.Data == null || idColumn.Data.Length == 0) continue;

            var count = idColumn.Data.Length;
            var id = ToStrings(idColumn, count);
            var recordVersionId = await ReadStringsAsync(rowGroupReader, dataFields, "RecordVersionId", count, cancellationToken);
            var sourceRecordId = await ReadStringsAsync(rowGroupReader, dataFields, "SourceRecordId", count, cancellationToken);
            var ingestionJobId = await ReadStringsAsync(rowGroupReader, dataFields, "IngestionJobId", count, cancellationToken);
            var inboundFileName = await ReadStringsAsync(rowGroupReader, dataFields, "InboundFileName", count, cancellationToken);
            var userName = await ReadStringsAsync(rowGroupReader, dataFields, "UserName", count, cancellationToken);
            var loadDate = await ReadDatesAsync(rowGroupReader, dataFields, "LoadDateUtc", count, cancellationToken);
            var updateDate = await ReadDatesAsync(rowGroupReader, dataFields, "LastUpdateDateUtc", count, cancellationToken);
            var fileName = await ReadStringsAsync(rowGroupReader, dataFields, "FindingFileName", count, cancellationToken);
            var fileFormat = await ReadStringsAsync(rowGroupReader, dataFields, "FindingFileFormat", count, cancellationToken);
            var fileSize = await ReadLongsAsync(rowGroupReader, dataFields, "FindingFileSizeBytes", count, cancellationToken);
            var currentLocation = await ReadStringsAsync(rowGroupReader, dataFields, "CurrentFileLocation", count, cancellationToken);
            var findingType = await ReadStringsAsync(rowGroupReader, dataFields, "FindingType", count, cancellationToken);
            var dataSystem = await ReadStringsAsync(rowGroupReader, dataFields, "DataSystem", count, cancellationToken);
            var originatingDataSystem = await ReadStringsAsync(rowGroupReader, dataFields, "OriginatingDataSystem", count, cancellationToken);
            var originatingVendorTool = await ReadStringsAsync(rowGroupReader, dataFields, "OriginatingVendorTool", count, cancellationToken);
            var status = await ReadStringsAsync(rowGroupReader, dataFields, "Status", count, cancellationToken);
            var statusColumn = await ReadStringsAsync(rowGroupReader, dataFields, "StatusColumnValue", count, cancellationToken);
            var errorCategory = await ReadStringsAsync(rowGroupReader, dataFields, "ErrorCategory", count, cancellationToken);
            var errorReason = await ReadStringsAsync(rowGroupReader, dataFields, "ErrorReason", count, cancellationToken);
            var lastModified = await ReadDatesAsync(rowGroupReader, dataFields, "LastModifiedDateUtc", count, cancellationToken);
            var created = await ReadDatesAsync(rowGroupReader, dataFields, "CreatedDateUtc", count, cancellationToken);
            var lastAccessed = await ReadDatesAsync(rowGroupReader, dataFields, "LastAccessedDateUtc", count, cancellationToken);
            var quarantineDate = await ReadDatesAsync(rowGroupReader, dataFields, "QuarantineDateUtc", count, cancellationToken);
            var restoredDate = await ReadDatesAsync(rowGroupReader, dataFields, "RestoredDateUtc", count, cancellationToken);
            var exceptionDate = await ReadDatesAsync(rowGroupReader, dataFields, "ExceptionDateUtc", count, cancellationToken);
            var deletedDate = await ReadDatesAsync(rowGroupReader, dataFields, "DeletedDateUtc", count, cancellationToken);
            var detectionDate = await ReadDatesAsync(rowGroupReader, dataFields, "DetectionDateUtc", count, cancellationToken);
            var siteOwner = await ReadStringsAsync(rowGroupReader, dataFields, "SiteOwner", count, cancellationToken);
            var fileOwner = await ReadStringsAsync(rowGroupReader, dataFields, "FileOwner", count, cancellationToken);
            var businessUnit = await ReadStringsAsync(rowGroupReader, dataFields, "BusinessUnit", count, cancellationToken);
            var division = await ReadStringsAsync(rowGroupReader, dataFields, "Division", count, cancellationToken);
            var department = await ReadStringsAsync(rowGroupReader, dataFields, "Department", count, cancellationToken);
            var region = await ReadStringsAsync(rowGroupReader, dataFields, "Region", count, cancellationToken);
            var country = await ReadStringsAsync(rowGroupReader, dataFields, "Country", count, cancellationToken);
            var policyName = await ReadStringsAsync(rowGroupReader, dataFields, "PolicyName", count, cancellationToken);
            var policyId = await ReadStringsAsync(rowGroupReader, dataFields, "PolicyId", count, cancellationToken);
            var findingReason = await ReadStringsAsync(rowGroupReader, dataFields, "FindingReason", count, cancellationToken);
            var riskLevel = await ReadStringsAsync(rowGroupReader, dataFields, "RiskLevel", count, cancellationToken);
            var sensitivityLabel = await ReadStringsAsync(rowGroupReader, dataFields, "SensitivityLabel", count, cancellationToken);
            var recommendedAction = await ReadStringsAsync(rowGroupReader, dataFields, "RecommendedAction", count, cancellationToken);
            var originalLocation = await ReadStringsAsync(rowGroupReader, dataFields, "OriginalFileLocation", count, cancellationToken);
            var restorationTicket = await ReadStringsAsync(rowGroupReader, dataFields, "RestorationTicketIdentifier", count, cancellationToken);
            var restorationEmail = await ReadStringsAsync(rowGroupReader, dataFields, "RestorationRequestorEmail", count, cancellationToken);
            var restorationComment = await ReadStringsAsync(rowGroupReader, dataFields, "RestorationComment", count, cancellationToken);

            for (var i = 0; i < count; i++)
            {
                if (globalIndex++ < lastProcessedRecordCount) continue;

                var storedStatus = string.IsNullOrWhiteSpace(statusColumn[i]) ? status[i] : statusColumn[i];
                var finding = new FileFinding
                {
                    Id = Guid.TryParse(id[i], out var parsedId) ? parsedId : Guid.NewGuid(),
                    RecordVersionId = recordVersionId[i],
                    SourceRecordId = NullIfWhiteSpace(sourceRecordId[i]),
                    IngestionJobId = NullIfWhiteSpace(ingestionJobId[i]),
                    InboundFileName = inboundFileName[i],
                    UserName = string.IsNullOrWhiteSpace(userName[i]) ? "System" : userName[i],
                    LoadDateUtc = ToNullableDate(loadDate[i]) ?? DateTime.UtcNow,
                    LastUpdateDateUtc = ToNullableDate(updateDate[i]) ?? DateTime.UtcNow,
                    FindingFileName = fileName[i],
                    FindingFileFormat = fileFormat[i],
                    FindingFileSizeBytes = fileSize[i],
                    CurrentFileLocation = currentLocation[i],
                    OriginatingDataSystem = originatingDataSystem[i],
                    OriginatingVendorTool = originatingVendorTool[i],
                    SourceSystemPlatform = NullIfWhiteSpace(dataSystem[i]),
                    ErrorCategory = NullIfWhiteSpace(errorCategory[i]),
                    LastModifiedDateUtc = ToNullableDate(lastModified[i]),
                    CreatedDateUtc = ToNullableDate(created[i]),
                    LastAccessedDateUtc = ToNullableDate(lastAccessed[i]),
                    QuarantineDateUtc = ToNullableDate(quarantineDate[i]),
                    RestoredDateUtc = ToNullableDate(restoredDate[i]),
                    ExceptionDateUtc = ToNullableDate(exceptionDate[i]),
                    DeletedDateUtc = ToNullableDate(deletedDate[i]),
                    DetectionDateUtc = ToNullableDate(detectionDate[i]),
                    SiteOwner = NullIfWhiteSpace(siteOwner[i]),
                    FileOwner = NullIfWhiteSpace(fileOwner[i]),
                    BusinessUnit = NullIfWhiteSpace(businessUnit[i]),
                    Division = NullIfWhiteSpace(division[i]),
                    Department = NullIfWhiteSpace(department[i]),
                    Region = NullIfWhiteSpace(region[i]),
                    Country = NullIfWhiteSpace(country[i]),
                    PolicyName = NullIfWhiteSpace(policyName[i]),
                    PolicyId = NullIfWhiteSpace(policyId[i]),
                    FindingReason = NullIfWhiteSpace(findingReason[i]),
                    RiskLevel = NullIfWhiteSpace(riskLevel[i]),
                    SensitivityLabel = NullIfWhiteSpace(sensitivityLabel[i]),
                    RecommendedAction = NullIfWhiteSpace(recommendedAction[i]),
                    OriginalFileLocation = NullIfWhiteSpace(originalLocation[i]),
                    RestorationTicketIdentifier = NullIfWhiteSpace(restorationTicket[i]),
                    RestorationRequestorEmail = NullIfWhiteSpace(restorationEmail[i]),
                    RestorationComment = NullIfWhiteSpace(restorationComment[i])
                };

                finding.FindingType = findingType[i];
                finding.Status = FileFinding.ResolveStatusFromStoredValue(storedStatus);
                finding.StatusColumnValue = string.IsNullOrWhiteSpace(storedStatus) ? finding.Status.ToString() : storedStatus;
                finding.ErrorReason = errorReason[i];
                output.Add(finding);
            }
        }

        _logger.LogInformation("[PARQUET_RESUME_READ_COMPLETE] Path:{Path}, RemainingRecords:{RemainingRecords}", workingFilePath, output.Count);
        return output;
    }

    private static async Task WriteRowsAsync(ParquetRowGroupWriter writer, Fields f, IReadOnlyList<FileFinding> rows, CancellationToken ct)
    {
        await writer.WriteAsync(f.Id, rows.Select(x => x.Id.ToString()).ToArray(), ct);
        await writer.WriteAsync(f.RecordVersionId, rows.Select(x => Clean(x.RecordVersionId)).ToArray(), ct);
        await writer.WriteAsync(f.SourceRecordId, rows.Select(x => Clean(x.SourceRecordId)).ToArray(), ct);
        await writer.WriteAsync(f.IngestionJobId, rows.Select(x => Clean(x.IngestionJobId)).ToArray(), ct);
        await writer.WriteAsync(f.InboundFileName, rows.Select(x => Clean(x.InboundFileName)).ToArray(), ct);
        await writer.WriteAsync(f.UserName, rows.Select(x => Clean(x.UserName)).ToArray(), ct);
        await writer.WriteAsync(f.LoadDateUtc, rows.Select(x => x.LoadDateUtc).ToArray(), ct);
        await writer.WriteAsync(f.LastUpdateDateUtc, rows.Select(x => x.LastUpdateDateUtc).ToArray(), ct);
        await writer.WriteAsync(f.FindingFileName, rows.Select(x => Clean(x.FindingFileName)).ToArray(), ct);
        await writer.WriteAsync(f.FindingFileFormat, rows.Select(x => Clean(x.FindingFileFormat)).ToArray(), ct);
        await writer.WriteAsync(f.FindingFileSizeBytes, rows.Select(x => x.FindingFileSizeBytes ?? 0L).ToArray(), ct);
        await writer.WriteAsync(f.CurrentFileLocation, rows.Select(x => Clean(x.CurrentFileLocation)).ToArray(), ct);
        await writer.WriteAsync(f.FindingType, rows.Select(x => Clean(x.FindingType)).ToArray(), ct);
        await writer.WriteAsync(f.DataSystem, rows.Select(x => Clean(x.DataSystem)).ToArray(), ct);
        await writer.WriteAsync(f.OriginatingDataSystem, rows.Select(x => Clean(x.OriginatingDataSystem)).ToArray(), ct);
        await writer.WriteAsync(f.OriginatingVendorTool, rows.Select(x => Clean(x.OriginatingVendorTool)).ToArray(), ct);
        await writer.WriteAsync(f.Status, rows.Select(x => x.Status.ToString()).ToArray(), ct);
        await writer.WriteAsync(f.StatusColumnValue, rows.Select(x => Clean(x.StatusColumnValue)).ToArray(), ct);
        await writer.WriteAsync(f.ErrorCategory, rows.Select(x => Clean(x.ErrorCategory)).ToArray(), ct);
        await writer.WriteAsync(f.ErrorReason, rows.Select(x => Clean(x.ErrorReason)).ToArray(), ct);
        await writer.WriteAsync(f.LastModifiedDateUtc, rows.Select(x => x.LastModifiedDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.CreatedDateUtc, rows.Select(x => x.CreatedDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.LastAccessedDateUtc, rows.Select(x => x.LastAccessedDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.QuarantineDateUtc, rows.Select(x => x.QuarantineDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.RestoredDateUtc, rows.Select(x => x.RestoredDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.ExceptionDateUtc, rows.Select(x => x.ExceptionDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.DeletedDateUtc, rows.Select(x => x.DeletedDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.DetectionDateUtc, rows.Select(x => x.DetectionDateUtc ?? DateTime.MinValue).ToArray(), ct);
        await writer.WriteAsync(f.SiteOwner, rows.Select(x => Clean(x.SiteOwner)).ToArray(), ct);
        await writer.WriteAsync(f.FileOwner, rows.Select(x => Clean(x.FileOwner)).ToArray(), ct);
        await writer.WriteAsync(f.BusinessUnit, rows.Select(x => Clean(x.BusinessUnit)).ToArray(), ct);
        await writer.WriteAsync(f.Division, rows.Select(x => Clean(x.Division)).ToArray(), ct);
        await writer.WriteAsync(f.Department, rows.Select(x => Clean(x.Department)).ToArray(), ct);
        await writer.WriteAsync(f.Region, rows.Select(x => Clean(x.Region)).ToArray(), ct);
        await writer.WriteAsync(f.Country, rows.Select(x => Clean(x.Country)).ToArray(), ct);
        await writer.WriteAsync(f.PolicyName, rows.Select(x => Clean(x.PolicyName)).ToArray(), ct);
        await writer.WriteAsync(f.PolicyId, rows.Select(x => Clean(x.PolicyId)).ToArray(), ct);
        await writer.WriteAsync(f.FindingReason, rows.Select(x => Clean(x.FindingReason)).ToArray(), ct);
        await writer.WriteAsync(f.RiskLevel, rows.Select(x => Clean(x.RiskLevel)).ToArray(), ct);
        await writer.WriteAsync(f.SensitivityLabel, rows.Select(x => Clean(x.SensitivityLabel)).ToArray(), ct);
        await writer.WriteAsync(f.RecommendedAction, rows.Select(x => Clean(x.RecommendedAction)).ToArray(), ct);
        await writer.WriteAsync(f.OriginalFileLocation, rows.Select(x => Clean(x.OriginalFileLocation)).ToArray(), ct);
        await writer.WriteAsync(f.RestorationTicketIdentifier, rows.Select(x => Clean(x.RestorationTicketIdentifier)).ToArray(), ct);
        await writer.WriteAsync(f.RestorationRequestorEmail, rows.Select(x => Clean(x.RestorationRequestorEmail)).ToArray(), ct);
        await writer.WriteAsync(f.RestorationComment, rows.Select(x => Clean(x.RestorationComment)).ToArray(), ct);
    }

    private static Fields CreateFields()
    {
        var f = new Fields();
        f.All = new DataField[]
        {
            f.Id, f.RecordVersionId, f.SourceRecordId, f.IngestionJobId, f.InboundFileName, f.UserName,
            f.LoadDateUtc, f.LastUpdateDateUtc, f.FindingFileName, f.FindingFileFormat, f.FindingFileSizeBytes,
            f.CurrentFileLocation, f.FindingType, f.DataSystem, f.OriginatingDataSystem, f.OriginatingVendorTool,
            f.Status, f.StatusColumnValue, f.ErrorCategory, f.ErrorReason, f.LastModifiedDateUtc, f.CreatedDateUtc,
            f.LastAccessedDateUtc, f.QuarantineDateUtc, f.RestoredDateUtc, f.ExceptionDateUtc, f.DeletedDateUtc,
            f.DetectionDateUtc, f.SiteOwner, f.FileOwner, f.BusinessUnit, f.Division, f.Department, f.Region,
            f.Country, f.PolicyName, f.PolicyId, f.FindingReason, f.RiskLevel, f.SensitivityLabel, f.RecommendedAction,
            f.OriginalFileLocation, f.RestorationTicketIdentifier, f.RestorationRequestorEmail, f.RestorationComment
        };
        return f;
    }

    private static async Task<DataColumn?> ReadColumnAsync(ParquetRowGroupReader reader, IReadOnlyDictionary<string, DataField> fields, string name, CancellationToken ct)
        => fields.TryGetValue(name, out var field) ? await reader.ReadColumnAsync(field, ct) : null;

    private static async Task<string[]> ReadStringsAsync(ParquetRowGroupReader reader, IReadOnlyDictionary<string, DataField> fields, string name, int count, CancellationToken ct)
        => ToStrings(await ReadColumnAsync(reader, fields, name, ct), count);

    private static async Task<long[]> ReadLongsAsync(ParquetRowGroupReader reader, IReadOnlyDictionary<string, DataField> fields, string name, int count, CancellationToken ct)
    {
        var column = await ReadColumnAsync(reader, fields, name, ct);
        if (column?.Data == null) return Enumerable.Repeat(0L, count).ToArray();
        return Pad(column.Data.Cast<object?>().Select(x => x switch
        {
            long value => value,
            int value => value,
            decimal value => Convert.ToInt64(value),
            double value => Convert.ToInt64(value),
            string value when long.TryParse(value, out var parsed) => parsed,
            _ => 0L
        }).ToArray(), count, 0L);
    }

    private static async Task<DateTime[]> ReadDatesAsync(ParquetRowGroupReader reader, IReadOnlyDictionary<string, DataField> fields, string name, int count, CancellationToken ct)
    {
        var column = await ReadColumnAsync(reader, fields, name, ct);
        if (column?.Data == null) return Enumerable.Repeat(DateTime.MinValue, count).ToArray();
        return Pad(column.Data.Cast<object?>().Select(x => x switch
        {
            DateTime value => value,
            DateTimeOffset value => value.UtcDateTime,
            string value when DateTime.TryParse(value, out var parsed) => parsed,
            _ => DateTime.MinValue
        }).ToArray(), count, DateTime.MinValue);
    }

    private static string[] ToStrings(DataColumn? column, int count)
        => column?.Data == null
            ? Enumerable.Repeat(string.Empty, count).ToArray()
            : Pad(column.Data.Cast<object?>().Select(x => x?.ToString()?.Trim() ?? string.Empty).ToArray(), count, string.Empty);

    private static T[] Pad<T>(IReadOnlyList<T> values, int count, T fallback)
    {
        if (values.Count == count) return values.ToArray();
        var output = new T[count];
        for (var i = 0; i < count; i++) output[i] = i < values.Count ? values[i] : fallback;
        return output;
    }

    private static DateTime? ToNullableDate(DateTime value) => value == DateTime.MinValue ? null : value;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private sealed class Fields
    {
        public DataField[] All { get; set; } = Array.Empty<DataField>();
        public DataField<string> Id { get; } = new("Id");
        public DataField<string> RecordVersionId { get; } = new("RecordVersionId");
        public DataField<string> SourceRecordId { get; } = new("SourceRecordId");
        public DataField<string> IngestionJobId { get; } = new("IngestionJobId");
        public DataField<string> InboundFileName { get; } = new("InboundFileName");
        public DataField<string> UserName { get; } = new("UserName");
        public DataField<DateTime> LoadDateUtc { get; } = new("LoadDateUtc");
        public DataField<DateTime> LastUpdateDateUtc { get; } = new("LastUpdateDateUtc");
        public DataField<string> FindingFileName { get; } = new("FindingFileName");
        public DataField<string> FindingFileFormat { get; } = new("FindingFileFormat");
        public DataField<long> FindingFileSizeBytes { get; } = new("FindingFileSizeBytes");
        public DataField<string> CurrentFileLocation { get; } = new("CurrentFileLocation");
        public DataField<string> FindingType { get; } = new("FindingType");
        public DataField<string> DataSystem { get; } = new("DataSystem");
        public DataField<string> OriginatingDataSystem { get; } = new("OriginatingDataSystem");
        public DataField<string> OriginatingVendorTool { get; } = new("OriginatingVendorTool");
        public DataField<string> Status { get; } = new("Status");
        public DataField<string> StatusColumnValue { get; } = new("StatusColumnValue");
        public DataField<string> ErrorCategory { get; } = new("ErrorCategory");
        public DataField<string> ErrorReason { get; } = new("ErrorReason");
        public DataField<DateTime> LastModifiedDateUtc { get; } = new("LastModifiedDateUtc");
        public DataField<DateTime> CreatedDateUtc { get; } = new("CreatedDateUtc");
        public DataField<DateTime> LastAccessedDateUtc { get; } = new("LastAccessedDateUtc");
        public DataField<DateTime> QuarantineDateUtc { get; } = new("QuarantineDateUtc");
        public DataField<DateTime> RestoredDateUtc { get; } = new("RestoredDateUtc");
        public DataField<DateTime> ExceptionDateUtc { get; } = new("ExceptionDateUtc");
        public DataField<DateTime> DeletedDateUtc { get; } = new("DeletedDateUtc");
        public DataField<DateTime> DetectionDateUtc { get; } = new("DetectionDateUtc");
        public DataField<string> SiteOwner { get; } = new("SiteOwner");
        public DataField<string> FileOwner { get; } = new("FileOwner");
        public DataField<string> BusinessUnit { get; } = new("BusinessUnit");
        public DataField<string> Division { get; } = new("Division");
        public DataField<string> Department { get; } = new("Department");
        public DataField<string> Region { get; } = new("Region");
        public DataField<string> Country { get; } = new("Country");
        public DataField<string> PolicyName { get; } = new("PolicyName");
        public DataField<string> PolicyId { get; } = new("PolicyId");
        public DataField<string> FindingReason { get; } = new("FindingReason");
        public DataField<string> RiskLevel { get; } = new("RiskLevel");
        public DataField<string> SensitivityLabel { get; } = new("SensitivityLabel");
        public DataField<string> RecommendedAction { get; } = new("RecommendedAction");
        public DataField<string> OriginalFileLocation { get; } = new("OriginalFileLocation");
        public DataField<string> RestorationTicketIdentifier { get; } = new("RestorationTicketIdentifier");
        public DataField<string> RestorationRequestorEmail { get; } = new("RestorationRequestorEmail");
        public DataField<string> RestorationComment { get; } = new("RestorationComment");
    }
}
