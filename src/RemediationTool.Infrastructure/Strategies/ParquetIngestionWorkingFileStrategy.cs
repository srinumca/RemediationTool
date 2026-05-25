using Parquet;
using Parquet.Schema;
using Parquet.File;
using Parquet.Data;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

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

        var idField = new DataField<string>("Id");
        var recordVersionIdField = new DataField<string>("RecordVersionId");
        var sourceRecordIdField = new DataField<string>("SourceRecordId");
        var ingestionJobIdField = new DataField<string>("IngestionJobId");
        var inboundFileNameField = new DataField<string>("InboundFileName");
        var userNameField = new DataField<string>("UserName");

        var loadDateUtcField = new DataField<DateTime>("LoadDateUtc");
        var lastUpdateDateUtcField = new DataField<DateTime>("LastUpdateDateUtc");

        var findingFileNameField = new DataField<string>("FindingFileName");
        var findingFileFormatField = new DataField<string>("FindingFileFormat");
        var findingFileSizeBytesField = new DataField<long>("FindingFileSizeBytes");
        var currentFileLocationField = new DataField<string>("CurrentFileLocation");
        var findingTypeField = new DataField<string>("FindingType");
        var originatingDataSystemField = new DataField<string>("OriginatingDataSystem");
        var originatingVendorToolField = new DataField<string>("OriginatingVendorTool");

        var lastModifiedDateUtcField = new DataField<DateTime>("LastModifiedDateUtc");
        var createdDateUtcField = new DataField<DateTime>("CreatedDateUtc");
        var lastAccessedDateUtcField = new DataField<DateTime>("LastAccessedDateUtc");

        var siteOwnerField = new DataField<string>("SiteOwner");
        var fileOwnerField = new DataField<string>("FileOwner");
        var businessUnitField = new DataField<string>("BusinessUnit");
        var divisionField = new DataField<string>("Division");
        var departmentField = new DataField<string>("Department");
        var regionField = new DataField<string>("Region");
        var countryField = new DataField<string>("Country");

        var policyNameField = new DataField<string>("PolicyName");
        var policyIdField = new DataField<string>("PolicyId");
        var findingReasonField = new DataField<string>("FindingReason");
        var riskLevelField = new DataField<string>("RiskLevel");
        var sensitivityLabelField = new DataField<string>("SensitivityLabel");
        var detectionDateUtcField = new DataField<DateTime>("DetectionDateUtc");
        var recommendedActionField = new DataField<string>("RecommendedAction");

        var originalFileLocationField = new DataField<string>("OriginalFileLocation");
        var quarantineDateUtcField = new DataField<DateTime>("QuarantineDateUtc");
        var restorationTicketIdentifierField = new DataField<string>("RestorationTicketIdentifier");
        var restorationRequestorEmailField = new DataField<string>("RestorationRequestorEmail");
        var restorationCommentField = new DataField<string>("RestorationComment");

        var schema = new ParquetSchema(
            idField,
            recordVersionIdField,
            sourceRecordIdField,
            ingestionJobIdField,
            inboundFileNameField,
            userNameField,
            loadDateUtcField,
            lastUpdateDateUtcField,
            findingFileNameField,
            findingFileFormatField,
            findingFileSizeBytesField,
            currentFileLocationField,
            findingTypeField,
            originatingDataSystemField,
            originatingVendorToolField,
            lastModifiedDateUtcField,
            createdDateUtcField,
            lastAccessedDateUtcField,
            siteOwnerField,
            fileOwnerField,
            businessUnitField,
            divisionField,
            departmentField,
            regionField,
            countryField,
            policyNameField,
            policyIdField,
            findingReasonField,
            riskLevelField,
            sensitivityLabelField,
            detectionDateUtcField,
            recommendedActionField,
            originalFileLocationField,
            quarantineDateUtcField,
            restorationTicketIdentifierField,
            restorationRequestorEmailField,
            restorationCommentField);

        await using var parquetStream = new MemoryStream();

        await using (var writer = await ParquetWriter.CreateAsync(
            schema,
            parquetStream,
            cancellationToken: cancellationToken))
        {
            using var rowGroupWriter = writer.CreateRowGroup();

            await rowGroupWriter.WriteAsync(idField, validFindings.Select(x => x.Id.ToString()).ToArray());
            await rowGroupWriter.WriteAsync(recordVersionIdField, validFindings.Select(x => NullToEmpty(x.RecordVersionId)).ToArray());
            await rowGroupWriter.WriteAsync(sourceRecordIdField, validFindings.Select(x => NullToEmpty(x.SourceRecordId)).ToArray());
            await rowGroupWriter.WriteAsync(ingestionJobIdField, validFindings.Select(x => NullToEmpty(x.IngestionJobId)).ToArray());
            await rowGroupWriter.WriteAsync(inboundFileNameField, validFindings.Select(x => NullToEmpty(x.InboundFileName)).ToArray());
            await rowGroupWriter.WriteAsync(userNameField, validFindings.Select(x => NullToEmpty(x.UserName)).ToArray());

            await rowGroupWriter.WriteAsync<DateTime>(loadDateUtcField, validFindings.Select(x => x.LoadDateUtc).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(lastUpdateDateUtcField, validFindings.Select(x => x.LastUpdateDateUtc).ToArray());

            await rowGroupWriter.WriteAsync(findingFileNameField, validFindings.Select(x => NullToEmpty(x.FindingFileName)).ToArray());
            await rowGroupWriter.WriteAsync(findingFileFormatField, validFindings.Select(x => NullToEmpty(x.FindingFileFormat)).ToArray());
            await rowGroupWriter.WriteAsync<long>(findingFileSizeBytesField, validFindings.Select(x => x.FindingFileSizeBytes ?? 0L).ToArray());
            await rowGroupWriter.WriteAsync(currentFileLocationField, validFindings.Select(x => NullToEmpty(x.CurrentFileLocation)).ToArray());
            await rowGroupWriter.WriteAsync(findingTypeField, validFindings.Select(x => NullToEmpty(x.FindingType)).ToArray());
            await rowGroupWriter.WriteAsync(originatingDataSystemField, validFindings.Select(x => NullToEmpty(x.OriginatingDataSystem)).ToArray());
            await rowGroupWriter.WriteAsync(originatingVendorToolField, validFindings.Select(x => NullToEmpty(x.OriginatingVendorTool)).ToArray());

            await rowGroupWriter.WriteAsync<DateTime>(lastModifiedDateUtcField, validFindings.Select(x => x.LastModifiedDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(createdDateUtcField, validFindings.Select(x => x.CreatedDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(lastAccessedDateUtcField, validFindings.Select(x => x.LastAccessedDateUtc ?? DateTime.MinValue).ToArray());

            await rowGroupWriter.WriteAsync(siteOwnerField, validFindings.Select(x => NullToEmpty(x.SiteOwner)).ToArray());
            await rowGroupWriter.WriteAsync(fileOwnerField, validFindings.Select(x => NullToEmpty(x.FileOwner)).ToArray());
            await rowGroupWriter.WriteAsync(businessUnitField, validFindings.Select(x => NullToEmpty(x.BusinessUnit)).ToArray());
            await rowGroupWriter.WriteAsync(divisionField, validFindings.Select(x => NullToEmpty(x.Division)).ToArray());
            await rowGroupWriter.WriteAsync(departmentField, validFindings.Select(x => NullToEmpty(x.Department)).ToArray());
            await rowGroupWriter.WriteAsync(regionField, validFindings.Select(x => NullToEmpty(x.Region)).ToArray());
            await rowGroupWriter.WriteAsync(countryField, validFindings.Select(x => NullToEmpty(x.Country)).ToArray());

            await rowGroupWriter.WriteAsync(policyNameField, validFindings.Select(x => NullToEmpty(x.PolicyName)).ToArray());
            await rowGroupWriter.WriteAsync(policyIdField, validFindings.Select(x => NullToEmpty(x.PolicyId)).ToArray());
            await rowGroupWriter.WriteAsync(findingReasonField, validFindings.Select(x => NullToEmpty(x.FindingReason)).ToArray());
            await rowGroupWriter.WriteAsync(riskLevelField, validFindings.Select(x => NullToEmpty(x.RiskLevel)).ToArray());
            await rowGroupWriter.WriteAsync(sensitivityLabelField, validFindings.Select(x => NullToEmpty(x.SensitivityLabel)).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(detectionDateUtcField, validFindings.Select(x => x.DetectionDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync(recommendedActionField, validFindings.Select(x => NullToEmpty(x.RecommendedAction)).ToArray());

            await rowGroupWriter.WriteAsync(originalFileLocationField, validFindings.Select(x => NullToEmpty(x.OriginalFileLocation)).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(quarantineDateUtcField, validFindings.Select(x => x.QuarantineDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync(restorationTicketIdentifierField, validFindings.Select(x => NullToEmpty(x.RestorationTicketIdentifier)).ToArray());
            await rowGroupWriter.WriteAsync(restorationRequestorEmailField, validFindings.Select(x => NullToEmpty(x.RestorationRequestorEmail)).ToArray());
            await rowGroupWriter.WriteAsync(restorationCommentField, validFindings.Select(x => NullToEmpty(x.RestorationComment)).ToArray());
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
    private static string NullToEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    public Task<List<FileFinding>> ReadAfterAsync(
    string workingFilePath,
    int lastProcessedRecordCount,
    CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Parquet read support is not enabled for the currently installed Parquet.Net API version. Resume will fall back to JSON staging.");
    }
}