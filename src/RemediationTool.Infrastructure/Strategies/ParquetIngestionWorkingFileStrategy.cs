using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ParquetIngestionWorkingFileStrategy> _logger;

    public ParquetIngestionWorkingFileStrategy(IStorageService storage, ILogger<ParquetIngestionWorkingFileStrategy> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public string Format => "Parquet";

    /// <summary>
    /// Writes a Parquet working file to the storage service with the provided valid findings.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="inboundFileName"></param>
    /// <param name="validFindings"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task<IngestionWorkingFileResult> WriteAsync(
        string jobId,
        string inboundFileName,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Writing Parquet working file. JobId: {JobId}, FileName: {FileName}, RecordCount: {RecordCount}", 
            jobId, inboundFileName, validFindings?.Count ?? 0);

        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogWarning("WriteAsync rejected: JobId is required");
            throw new ArgumentException("JobId is required.", nameof(jobId));
        }

        if (validFindings == null)
        {
            _logger.LogWarning("WriteAsync rejected: ValidFindings is null");
            throw new ArgumentNullException(nameof(validFindings));
        }

        try
        {
            var workingFilePath = IngestionWorkingFilePathBuilder.BuildParquetPath(
                jobId, inboundFileName, DateTime.UtcNow);

            _logger.LogDebug("Parquet working file path: {WorkingFilePath}", workingFilePath);

            // System-generated fields
            var idField = new DataField<string>("Id");
            var recordVersionIdField = new DataField<string>("RecordVersionId");
            var sourceRecordIdField = new DataField<string>("SourceRecordId");
            var ingestionJobIdField = new DataField<string>("IngestionJobId");
            var inboundFileNameField = new DataField<string>("InboundFileName");
            var userNameField = new DataField<string>("UserName");
            var loadDateUtcField = new DataField<DateTime>("LoadDateUtc");
            var lastUpdateDateUtcField = new DataField<DateTime>("LastUpdateDateUtc");

            // Core finding fields
            var findingFileNameField = new DataField<string>("FindingFileName");
            var findingFileFormatField = new DataField<string>("FindingFileFormat");
            var findingFileSizeBytesField = new DataField<long>("FindingFileSizeBytes");
        var currentFileLocationField = new DataField<string>("CurrentFileLocation");
        var findingTypeField = new DataField<string>("FindingType");   // stored as enum string
        var dataSystemField = new DataField<string>("DataSystem");
        var originatingDataSystemField = new DataField<string>("OriginatingDataSystem");
        var originatingVendorToolField = new DataField<string>("OriginatingVendorTool");

        // Date fields
        var lastModifiedDateUtcField = new DataField<DateTime>("LastModifiedDateUtc");
        var createdDateUtcField = new DataField<DateTime>("CreatedDateUtc");
        var lastAccessedDateUtcField = new DataField<DateTime>("LastAccessedDateUtc");
        var quarantineDateUtcField = new DataField<DateTime>("QuarantineDateUtc");
        var restoredDateUtcField = new DataField<DateTime>("RestoredDateUtc");
        var exceptionDateUtcField = new DataField<DateTime>("ExceptionDateUtc");
        var deletedDateUtcField = new DataField<DateTime>("DeletedDateUtc");
        var detectionDateUtcField = new DataField<DateTime>("DetectionDateUtc");

        // Ownership / classification fields
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
        var recommendedActionField = new DataField<string>("RecommendedAction");

        // Location fields
        var originalFileLocationField = new DataField<string>("OriginalFileLocation");

        // Restoration fields
        var restorationTicketIdentifierField = new DataField<string>("RestorationTicketIdentifier");
        var restorationRequestorEmailField = new DataField<string>("RestorationRequestorEmail");
        var restorationCommentField = new DataField<string>("RestorationComment");

        var schema = new ParquetSchema(
            idField, recordVersionIdField, sourceRecordIdField, ingestionJobIdField,
            inboundFileNameField, userNameField, loadDateUtcField, lastUpdateDateUtcField,
            findingFileNameField, findingFileFormatField, findingFileSizeBytesField,
            currentFileLocationField, findingTypeField, dataSystemField,
            originatingDataSystemField, originatingVendorToolField,
            lastModifiedDateUtcField, createdDateUtcField, lastAccessedDateUtcField,
            quarantineDateUtcField, restoredDateUtcField, exceptionDateUtcField, deletedDateUtcField, detectionDateUtcField,
            siteOwnerField, fileOwnerField, businessUnitField, divisionField, departmentField,
            regionField, countryField, policyNameField, policyIdField,
            findingReasonField, riskLevelField, sensitivityLabelField, recommendedActionField,
            originalFileLocationField,
            restorationTicketIdentifierField, restorationRequestorEmailField, restorationCommentField);

        await using var parquetStream = new MemoryStream();

        await using (var writer = await ParquetWriter.CreateAsync(schema, parquetStream, cancellationToken: cancellationToken))
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

            // FindingType is already a plain string
            await rowGroupWriter.WriteAsync(findingTypeField, validFindings.Select(x => NullToEmpty(x.FindingType)).ToArray());
            await rowGroupWriter.WriteAsync(dataSystemField, validFindings.Select(x => NullToEmpty(x.DataSystem)).ToArray());

            await rowGroupWriter.WriteAsync(originatingDataSystemField, validFindings.Select(x => NullToEmpty(x.OriginatingDataSystem)).ToArray());
            await rowGroupWriter.WriteAsync(originatingVendorToolField, validFindings.Select(x => NullToEmpty(x.OriginatingVendorTool)).ToArray());

            await rowGroupWriter.WriteAsync<DateTime>(lastModifiedDateUtcField, validFindings.Select(x => x.LastModifiedDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(createdDateUtcField, validFindings.Select(x => x.CreatedDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(lastAccessedDateUtcField, validFindings.Select(x => x.LastAccessedDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(quarantineDateUtcField, validFindings.Select(x => x.QuarantineDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(restoredDateUtcField, validFindings.Select(x => x.RestoredDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(exceptionDateUtcField, validFindings.Select(x => x.ExceptionDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(deletedDateUtcField, validFindings.Select(x => x.DeletedDateUtc ?? DateTime.MinValue).ToArray());
            await rowGroupWriter.WriteAsync<DateTime>(detectionDateUtcField, validFindings.Select(x => x.DetectionDateUtc ?? DateTime.MinValue).ToArray());

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
            await rowGroupWriter.WriteAsync(recommendedActionField, validFindings.Select(x => NullToEmpty(x.RecommendedAction)).ToArray());

            await rowGroupWriter.WriteAsync(originalFileLocationField, validFindings.Select(x => NullToEmpty(x.OriginalFileLocation)).ToArray());

            await rowGroupWriter.WriteAsync(restorationTicketIdentifierField, validFindings.Select(x => NullToEmpty(x.RestorationTicketIdentifier)).ToArray());
            await rowGroupWriter.WriteAsync(restorationRequestorEmailField, validFindings.Select(x => NullToEmpty(x.RestorationRequestorEmail)).ToArray());
            await rowGroupWriter.WriteAsync(restorationCommentField, validFindings.Select(x => NullToEmpty(x.RestorationComment)).ToArray());
        }

        parquetStream.Position = 0;
        await _storage.UploadAsync(workingFilePath, parquetStream);

        _logger.LogInformation("Parquet working file written successfully. JobId: {JobId}, FilePath: {FilePath}, RecordCount: {RecordCount}, SizeBytes: {SizeBytes}", 
            jobId, workingFilePath, validFindings.Count, parquetStream.Length);

        return new IngestionWorkingFileResult
        {
            Format = Format,
            Path = workingFilePath,
            RecordCount = validFindings.Count
        };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing Parquet working file. JobId: {JobId}, FileName: {FileName}", jobId, inboundFileName);
            throw;
        }
    }

    /// <summary>
    /// Converts a nullable string to an empty string if it is null or whitespace, otherwise trims the string.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static string NullToEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    /// <summary>
    /// Reads a Parquet working file from the storage service starting after the specified last processed record count.
    /// </summary>
    /// <param name="workingFilePath"></param>
    /// <param name="lastProcessedRecordCount"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public Task<List<FileFinding>> ReadAfterAsync(
        string workingFilePath, int lastProcessedRecordCount,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Parquet read support is not enabled for the currently installed Parquet.Net API version. Resume will fall back to JSON staging.");
    }
}