using Parquet;
using Parquet.Schema;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Infrastructure.Strategies;

public class ParquetIngestionWorkingFileStrategy : IIngestionWorkingFileStrategy
{
    private readonly IStorageService _storage;

    public ParquetIngestionWorkingFileStrategy(IStorageService storage)
    {
        _storage = storage;
    }

    public string Format => "Parquet";

    public async Task<IngestionWorkingFileResult> WriteAsync(
        string jobId,
        string inboundFileName,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null)
            throw new ArgumentNullException(nameof(validFindings));

        var workingFilePath = IngestionWorkingFilePathBuilder.BuildParquetPath(
            jobId,
            inboundFileName,
            DateTime.UtcNow);

        var schema = BuildSchema();

        await using var parquetStream = new MemoryStream();

        await using (var writer = await ParquetWriter.CreateAsync(
            schema,
            parquetStream,
            cancellationToken: cancellationToken))
        {
            using var rg = writer.CreateRowGroup();

            await rg.WriteAsync(GetField(schema, "Id"), validFindings.Select(x => x.Id.ToString()).ToArray());
            await rg.WriteAsync(GetField(schema, "RecordVersionId"), validFindings.Select(x => NullToEmpty(x.RecordVersionId)).ToArray());
            await rg.WriteAsync(GetField(schema, "SourceRecordId"), validFindings.Select(x => NullToEmpty(x.SourceRecordId)).ToArray());
            await rg.WriteAsync(GetField(schema, "IngestionJobId"), validFindings.Select(x => NullToEmpty(x.IngestionJobId)).ToArray());
            await rg.WriteAsync(GetField(schema, "InboundFileName"), validFindings.Select(x => NullToEmpty(x.InboundFileName)).ToArray());
            await rg.WriteAsync(GetField(schema, "UserName"), validFindings.Select(x => NullToEmpty(x.UserName)).ToArray());

            await rg.WriteAsync<DateTime>(GetField(schema, "LoadDateUtc"), validFindings.Select(x => x.LoadDateUtc).ToArray());
            await rg.WriteAsync<DateTime>(GetField(schema, "LastUpdateDateUtc"), validFindings.Select(x => x.LastUpdateDateUtc).ToArray());

            await rg.WriteAsync(GetField(schema, "FindingFileName"), validFindings.Select(x => NullToEmpty(x.FindingFileName)).ToArray());
            await rg.WriteAsync(GetField(schema, "FindingFileFormat"), validFindings.Select(x => NullToEmpty(x.FindingFileFormat)).ToArray());

            await rg.WriteAsync<long>(GetField(schema, "FindingFileSizeBytes"), validFindings.Select(x => x.FindingFileSizeBytes ?? 0L).ToArray());

            await rg.WriteAsync(GetField(schema, "CurrentFileLocation"), validFindings.Select(x => NullToEmpty(x.CurrentFileLocation)).ToArray());
            await rg.WriteAsync(GetField(schema, "FindingType"), validFindings.Select(x => x.FindingType.ToString()).ToArray());
            await rg.WriteAsync(GetField(schema, "OriginatingDataSystem"), validFindings.Select(x => NullToEmpty(x.OriginatingDataSystem)).ToArray());
            await rg.WriteAsync(GetField(schema, "OriginatingVendorTool"), validFindings.Select(x => NullToEmpty(x.OriginatingVendorTool)).ToArray());


            await rg.WriteAsync(GetField(schema, "SiteOwner"), validFindings.Select(x => NullToEmpty(x.SiteOwner)).ToArray());
            await rg.WriteAsync(GetField(schema, "FileOwner"), validFindings.Select(x => NullToEmpty(x.FileOwner)).ToArray());
            await rg.WriteAsync(GetField(schema, "OriginalFileLocation"), validFindings.Select(x => NullToEmpty(x.OriginalFileLocation)).ToArray());

            await rg.WriteAsync<DateTime>(GetField(schema, "QuarantineDateUtc"), validFindings.Select(x => x.QuarantineDateUtc ?? DateTime.MinValue).ToArray());

            await rg.WriteAsync(GetField(schema, "RestorationTicketIdentifier"), validFindings.Select(x => NullToEmpty(x.RestorationTicketIdentifier)).ToArray());
            await rg.WriteAsync(GetField(schema, "RestorationRequestorEmail"), validFindings.Select(x => NullToEmpty(x.RestorationRequestorEmail)).ToArray());
            await rg.WriteAsync(GetField(schema, "RestorationComment"), validFindings.Select(x => NullToEmpty(x.RestorationComment)).ToArray());
            await rg.WriteAsync(GetField(schema, "DataSystem"), validFindings.Select(x => NullToEmpty(x.DataSystem)).ToArray());
        }

        parquetStream.Position = 0;

        await _storage.UploadAsync(workingFilePath, parquetStream);

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
        if (string.IsNullOrWhiteSpace(workingFilePath))
            throw new ArgumentException("Working file path is required.", nameof(workingFilePath));

        var stream = await _storage.DownloadAsync(workingFilePath);

        await using var reader = await ParquetReader.CreateAsync(
            stream,
            cancellationToken: cancellationToken);

        var dataFields = reader.Schema.GetDataFields();

        var fieldMap = dataFields.ToDictionary(
            f => f.Name,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        var allFindings = new List<FileFinding>();

        for (var groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
        {
            using var rg = reader.OpenRowGroupReader(groupIndex);

            var rowCount = checked((int)rg.RowCount);

            async Task<string[]> ReadStringAsync(string name)
            {
                var values = new string[rowCount];
                await rg.ReadAsync(GetField(fieldMap, name), values);
                return values;
            }

            async Task<DateTime[]> ReadDateTimeAsync(string name)
            {
                var values = new DateTime[rowCount];
                await rg.ReadAsync<DateTime>(GetField(fieldMap, name), values);
                return values;
            }

            async Task<long[]> ReadLongAsync(string name)
            {
                var values = new long[rowCount];
                await rg.ReadAsync<long>(GetField(fieldMap, name), values);
                return values;
            }

            var ids = await ReadStringAsync("Id");
            var recordVersionIds = await ReadStringAsync("RecordVersionId");
            var sourceRecordIds = await ReadStringAsync("SourceRecordId");
            var ingestionJobIds = await ReadStringAsync("IngestionJobId");
            var inboundFileNames = await ReadStringAsync("InboundFileName");
            var userNames = await ReadStringAsync("UserName");

            var loadDates = await ReadDateTimeAsync("LoadDateUtc");
            var lastUpdateDates = await ReadDateTimeAsync("LastUpdateDateUtc");

            var findingFileNames = await ReadStringAsync("FindingFileName");
            var findingFileFormats = await ReadStringAsync("FindingFileFormat");
            var findingFileSizes = await ReadLongAsync("FindingFileSizeBytes");
            var currentFileLocations = await ReadStringAsync("CurrentFileLocation");
            var findingTypes = await ReadStringAsync("FindingType");
            var originatingDataSystems = await ReadStringAsync("OriginatingDataSystem");
            var originatingVendorTools = await ReadStringAsync("OriginatingVendorTool");

            var lastModifiedDates = await ReadDateTimeAsync("LastModifiedDateUtc");
            var createdDates = await ReadDateTimeAsync("CreatedDateUtc");
            var lastAccessedDates = await ReadDateTimeAsync("LastAccessedDateUtc");

            var siteOwners = await ReadStringAsync("SiteOwner");
            var fileOwners = await ReadStringAsync("FileOwner");
            var businessUnits = await ReadStringAsync("BusinessUnit");
            var divisions = await ReadStringAsync("Division");
            var departments = await ReadStringAsync("Department");
            var regions = await ReadStringAsync("Region");
            var countries = await ReadStringAsync("Country");
            var policyNames = await ReadStringAsync("PolicyName");
            var policyIds = await ReadStringAsync("PolicyId");
            var findingReasons = await ReadStringAsync("FindingReason");
            var riskLevels = await ReadStringAsync("RiskLevel");
            var sensitivityLabels = await ReadStringAsync("SensitivityLabel");

            var detectionDates = await ReadDateTimeAsync("DetectionDateUtc");

            var recommendedActions = await ReadStringAsync("RecommendedAction");
            var originalFileLocations = await ReadStringAsync("OriginalFileLocation");

            var quarantineDates = await ReadDateTimeAsync("QuarantineDateUtc");

            var restorationTicketIdentifiers = await ReadStringAsync("RestorationTicketIdentifier");
            var restorationRequestorEmails = await ReadStringAsync("RestorationRequestorEmail");
            var restorationComments = await ReadStringAsync("RestorationComment");
            var dataSystems = await ReadStringAsync("DataSystem");

            for (var i = 0; i < rowCount; i++)
            {
                Enum.TryParse<FindingType>(
                    findingTypes[i],
                    ignoreCase: true,
                    out var parsedFindingType);

                allFindings.Add(new FileFinding
                {
                    Id = Guid.TryParse(ids[i], out var parsedId) ? parsedId : Guid.NewGuid(),

                    RecordVersionId = NullIfEmpty(recordVersionIds[i]),
                    SourceRecordId = NullIfEmpty(sourceRecordIds[i]),
                    IngestionJobId = NullIfEmpty(ingestionJobIds[i]),
                    InboundFileName = NullIfEmpty(inboundFileNames[i]),
                    UserName = NullIfEmpty(userNames[i]),

                    LoadDateUtc = loadDates[i],
                    LastUpdateDateUtc = lastUpdateDates[i],

                    FindingFileName = NullIfEmpty(findingFileNames[i]) ?? string.Empty,
                    FindingFileFormat = NullIfEmpty(findingFileFormats[i]),
                    FindingFileSizeBytes = findingFileSizes[i] == 0L ? null : findingFileSizes[i],
                    CurrentFileLocation = NullIfEmpty(currentFileLocations[i]),

                    FindingType = parsedFindingType.ToString(),

                    OriginatingDataSystem = NullIfEmpty(originatingDataSystems[i]),
                    OriginatingVendorTool = NullIfEmpty(originatingVendorTools[i]),

                    SiteOwner = NullIfEmpty(siteOwners[i]),
                    FileOwner = NullIfEmpty(fileOwners[i]),
                    OriginalFileLocation = NullIfEmpty(originalFileLocations[i]),

                    QuarantineDateUtc = ToNullableDate(quarantineDates[i]),

                    RestorationTicketIdentifier = NullIfEmpty(restorationTicketIdentifiers[i]),
                    RestorationRequestorEmail = NullIfEmpty(restorationRequestorEmails[i]),
                    RestorationComment = NullIfEmpty(restorationComments[i]),

                    DataSystem = NullIfEmpty(dataSystems[i]) ?? string.Empty
                });
            }
        }

        return lastProcessedRecordCount <= 0
            ? allFindings
            : allFindings.Skip(lastProcessedRecordCount).ToList();
    }

    private static ParquetSchema BuildSchema() => new(
        new DataField<string>("Id"),
        new DataField<string>("RecordVersionId"),
        new DataField<string>("SourceRecordId"),
        new DataField<string>("IngestionJobId"),
        new DataField<string>("InboundFileName"),
        new DataField<string>("UserName"),

        new DataField<DateTime>("LoadDateUtc"),
        new DataField<DateTime>("LastUpdateDateUtc"),

        new DataField<string>("FindingFileName"),
        new DataField<string>("FindingFileFormat"),
        new DataField<long>("FindingFileSizeBytes"),
        new DataField<string>("CurrentFileLocation"),
        new DataField<string>("FindingType"),
        new DataField<string>("OriginatingDataSystem"),
        new DataField<string>("OriginatingVendorTool"),
        new DataField<string>("SiteOwner"),
        new DataField<string>("FileOwner"),
        new DataField<string>("OriginalFileLocation"),
        new DataField<DateTime>("QuarantineDateUtc"),
        new DataField<string>("RestorationTicketIdentifier"),
        new DataField<string>("RestorationRequestorEmail"),
        new DataField<string>("RestorationComment"),
        new DataField<string>("DataSystem"));

    private static DataField GetField(ParquetSchema schema, string name)
    {
        return schema.GetDataFields()
            .First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static DataField GetField(Dictionary<string, DataField> fieldMap, string name)
    {
        if (fieldMap.TryGetValue(name, out var field))
            return field;

        throw new InvalidOperationException(
            $"Parquet schema is missing expected field '{name}'. " +
            "The working file may have been written by an older schema version.");
    }

    private static string NullToEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static DateTime? ToNullableDate(DateTime value)
    {
        return value == DateTime.MinValue
            ? null
            : value;
    }
}