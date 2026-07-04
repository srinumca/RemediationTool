using Amazon.DynamoDBv2;

using Amazon.DynamoDBv2.Model;

using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options;

using RemediationTool.Application.Repositories;

using RemediationTool.Domain.Entities;

using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>

/// DynamoDB implementation of IIngestionJobAuditRepository.

/// Table: gfr-edg-reports-dev

/// Primary key: uid (HASH) — set to same value as ReportUid.

/// All attribute names are camelCase except UploadedBy, UploadedDisplayName,

/// UploadedEmailId which are PascalCase per the table schema.

/// </summary>

public class DynamoDbIngestionJobAuditRepository : IIngestionJobAuditRepository

{

    private readonly IAmazonDynamoDB _dynamoDb;

    private readonly string _tableName;

    private readonly ILogger<DynamoDbIngestionJobAuditRepository> _logger;

    public DynamoDbIngestionJobAuditRepository(

        IAmazonDynamoDB dynamoDb,

        IOptions<DynamoDbOptions> options,

        ILogger<DynamoDbIngestionJobAuditRepository> logger)

    {

        _dynamoDb = dynamoDb;

        _tableName = options.Value.JobAuditTableName;

        _logger = logger;

        _logger.LogInformation("DynamoDbIngestionJobAuditRepository initialized with table: {TableName}", _tableName);

    }

    /// <summary>

    /// Gets all IngestionJobAudit records from the DynamoDB table.

    /// </summary>

    /// <returns></returns>

    public List<IngestionJobAudit> GetAll()

    {

        _logger.LogDebug("Getting all IngestionJobAudit records from DynamoDB");

        try

        {

            var audits = new List<IngestionJobAudit>();

            Dictionary<string, AttributeValue>? lastKey = null;

            do

            {

                var response = _dynamoDb.ScanAsync(new ScanRequest

                {

                    TableName = _tableName,

                    ExclusiveStartKey = lastKey

                }).GetAwaiter().GetResult();

                audits.AddRange(response.Items.Select(DynamoDbAttributeMap.ToIngestionJobAudit));

                lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;

            }

            while (lastKey != null);

            _logger.LogInformation("Retrieved {Count} IngestionJobAudit records from DynamoDB", audits.Count);

            return audits;

        }

        catch (Exception ex)

        {

            _logger.LogError(ex, "Error getting all IngestionJobAudit records from DynamoDB");

            throw;

        }

    }

    /// <summary>

    /// Gets an IngestionJobAudit record by JobId from the DynamoDB table.

    /// </summary>

    /// <param name="jobId"></param>

    /// <returns></returns>

    public IngestionJobAudit? GetByJobId(string jobId)

    {

        _logger.LogDebug("Getting IngestionJobAudit by JobId: {JobId}", jobId);

        if (string.IsNullOrWhiteSpace(jobId))

        {

            _logger.LogWarning("GetByJobId called with null or empty JobId");

            return null;

        }

        try

        {

            // gfr-edg-reports-dev partition key is "uid" (renamed from "jobId"

            // in the old gfr-file-metadata-dev table). The value is the same

            // — it equals the reportUid — but the attribute name must match

            // the table schema exactly or DynamoDB throws

            // "The provided key element does not match the schema".

            var response = _dynamoDb.GetItemAsync(new GetItemRequest

            {

                TableName = _tableName,

                Key = new Dictionary<string, AttributeValue>

                {

                    ["uid"] = new AttributeValue { S = jobId }

                }

            }).GetAwaiter().GetResult();

            var result = response.Item?.Count > 0

                ? DynamoDbAttributeMap.ToIngestionJobAudit(response.Item)

                : null;

            if (result != null)

                _logger.LogDebug("IngestionJobAudit found. JobId: {JobId}, Status: {Status}", jobId, result.Status);

            else

                _logger.LogDebug("IngestionJobAudit not found. JobId: {JobId}", jobId);

            return result;

        }

        catch (Exception ex)

        {

            _logger.LogError(ex, "Error getting IngestionJobAudit by JobId. JobId: {JobId}", jobId);

            throw;

        }

    }

    /// <summary>

    /// Adds a new IngestionJobAudit record to the DynamoDB table.

    /// </summary>

    /// <param name="audit"></param>

    public void Add(IngestionJobAudit audit)

    {

        _logger.LogDebug("Adding IngestionJobAudit. JobId: {JobId}, Status: {Status}", audit.JobId, audit.Status);

        try

        {

            _dynamoDb.PutItemAsync(new PutItemRequest

            {

                TableName = _tableName,

                Item = DynamoDbAttributeMap.ToMap(audit)

            }).GetAwaiter().GetResult();

            _logger.LogDebug("IngestionJobAudit added successfully. JobId: {JobId}", audit.JobId);

        }

        catch (Exception ex)

        {

            _logger.LogError(ex, "Error adding IngestionJobAudit. JobId: {JobId}", audit.JobId);

            throw;

        }

    }

    /// <summary>

    /// Updates an existing IngestionJobAudit record in the DynamoDB table.

    /// </summary>

    /// <param name="audit"></param>

    public void Update(IngestionJobAudit audit)

    {

        _logger.LogDebug("Updating IngestionJobAudit. JobId: {JobId}, Status: {Status}", audit.JobId, audit.Status);

        try

        {

            _dynamoDb.PutItemAsync(new PutItemRequest

            {

                TableName = _tableName,

                Item = DynamoDbAttributeMap.ToMap(audit)

            }).GetAwaiter().GetResult();

            _logger.LogDebug("IngestionJobAudit updated successfully. JobId: {JobId}", audit.JobId);

        }

        catch (Exception ex)

        {

            _logger.LogError(ex, "Error updating IngestionJobAudit. JobId: {JobId}", audit.JobId);

            throw;

        }

    }

}
