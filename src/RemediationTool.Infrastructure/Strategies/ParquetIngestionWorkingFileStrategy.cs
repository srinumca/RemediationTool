using Parquet;
using Parquet.Data;
using Parquet.Schema;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;
using System.Data;

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

        await using var parquetStream = new MemoryStream();

        var schema = new Schema(
            new DataField<string>("Id"),
            new DataField<string>("RecordVersionId"),
            new DataField<string>("SourceRecordId"),
            new DataField<string>("IngestionJobId"),
            new DataField<string>("InboundFileName"),
            new DataField<string>("UserName"),

            new DataField<DateTime?>("LoadDateUtc"),
            new DataField<DateTime?>("LastUpdateDateUtc"),

            new DataField<string>("FindingFileName"),
            new DataField<string>("FindingFileFormat"),
            new DataField<long?>("FindingFileSizeBytes"),
            new DataField<string>("CurrentFileLocation"),
            new DataField<string>("FindingType"),
            new DataField<string>("OriginatingDataSystem"),
            new DataField<string>("OriginatingVendorTool"),

            new DataField<DateTime?>("LastModifiedDateUtc"),
            new DataField<DateTime?>("CreatedDateUtc"),
            new DataField<DateTime?>("LastAccessedDateUtc"),

            new DataField<string>("SiteOwner"),
            new DataField<string>("FileOwner"),
            new DataField<string>("BusinessUnit"),
            new DataField<string>("Division"),
            new DataField<string>("Department"),
            new DataField<string>("Region"),
            new DataField<string>("Country"),

            new DataField<string>("PolicyName"),
            new DataField<string>("PolicyId"),
            new DataField<string>("FindingReason"),
            new DataField<string>("RiskLevel"),
            new DataField<string>("SensitivityLabel"),
            new DataField<DateTime?>("DetectionDateUtc"),
            new DataField<string>("RecommendedAction"),

            new DataField<string>("OriginalFileLocation"),
            new DataField<DateTime?>("QuarantineDateUtc"),
            new DataField<string>("RestorationTicketIdentifier"),
            new DataField<string>("RestorationRequestorEmail"),
            new DataField<string>("RestorationComment")
        );

        using (var writer = await ParquetWriter.CreateAsync(schema, parquetStream, cancellationToken: cancellationToken))
        {
            using var rowGroupWriter = writer.CreateRowGroup();

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[0],
                validFindings.Select(x => x.Id.ToString()).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[1],
                validFindings.Select(x => x.RecordVersionId).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[2],
                validFindings.Select(x => x.SourceRecordId).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[3],
                validFindings.Select(x => x.IngestionJobId).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[4],
                validFindings.Select(x => x.InboundFileName).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[5],
                validFindings.Select(x => x.UserName).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[6],
                validFindings.Select(x => (DateTime?)x.LoadDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[7],
                validFindings.Select(x => (DateTime?)x.LastUpdateDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[8],
                validFindings.Select(x => x.FindingFileName).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[9],
                validFindings.Select(x => x.FindingFileFormat).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[10],
                validFindings.Select(x => x.FindingFileSizeBytes).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[11],
                validFindings.Select(x => x.CurrentFileLocation).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[12],
                validFindings.Select(x => x.FindingType).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[13],
                validFindings.Select(x => x.OriginatingDataSystem).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[14],
                validFindings.Select(x => x.OriginatingVendorTool).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[15],
                validFindings.Select(x => x.LastModifiedDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[16],
                validFindings.Select(x => x.CreatedDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[17],
                validFindings.Select(x => x.LastAccessedDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[18],
                validFindings.Select(x => x.SiteOwner).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[19],
                validFindings.Select(x => x.FileOwner).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[20],
                validFindings.Select(x => x.BusinessUnit).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[21],
                validFindings.Select(x => x.Division).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[22],
                validFindings.Select(x => x.Department).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[23],
                validFindings.Select(x => x.Region).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[24],
                validFindings.Select(x => x.Country).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[25],
                validFindings.Select(x => x.PolicyName).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[26],
                validFindings.Select(x => x.PolicyId).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[27],
                validFindings.Select(x => x.FindingReason).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[28],
                validFindings.Select(x => x.RiskLevel).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[29],
                validFindings.Select(x => x.SensitivityLabel).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[30],
                validFindings.Select(x => x.DetectionDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[31],
                validFindings.Select(x => x.RecommendedAction).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[32],
                validFindings.Select(x => x.OriginalFileLocation).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[33],
                validFindings.Select(x => x.QuarantineDateUtc).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[34],
                validFindings.Select(x => x.RestorationTicketIdentifier).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[35],
                validFindings.Select(x => x.RestorationRequestorEmail).ToArray()), cancellationToken);

            await rowGroupWriter.WriteColumnAsync(new DataColumn(
                (DataField)schema.Fields[36],
                validFindings.Select(x => x.RestorationComment).ToArray()), cancellationToken);
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
}