using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation of the ingestion job audit repository.
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
        _logger.LogInformation(
            "DynamoDbIngestionJobAuditRepository initialized with table: {TableName}",
            _tableName);
    }

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
            {
                _logger.LogDebug(
                    "IngestionJobAudit found. JobId: {JobId}, Status: {Status}",
                    jobId,
                    result.Status);
            }
            else
            {
                _logger.LogDebug("IngestionJobAudit not found. JobId: {JobId}", jobId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error getting IngestionJobAudit by JobId. JobId: {JobId}",
                jobId);
            throw;
        }
    }

    public void Add(IngestionJobAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        _logger.LogDebug(
            "Adding IngestionJobAudit. JobId: {JobId}, Status: {Status}",
            audit.JobId,
            audit.Status);

        try
        {
            _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item = DynamoDbAttributeMap.ToMap(audit)
            }).GetAwaiter().GetResult();

            _logger.LogDebug(
                "IngestionJobAudit added successfully. JobId: {JobId}",
                audit.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error adding IngestionJobAudit. JobId: {JobId}",
                audit.JobId);
            throw;
        }
    }

    public void Update(IngestionJobAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        _logger.LogDebug(
            "Updating IngestionJobAudit. JobId: {JobId}, Status: {Status}",
            audit.JobId,
            audit.Status);

        try
        {
            _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item = DynamoDbAttributeMap.ToMap(audit)
            }).GetAwaiter().GetResult();

            _logger.LogDebug(
                "IngestionJobAudit updated successfully. JobId: {JobId}",
                audit.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating IngestionJobAudit. JobId: {JobId}",
                audit.JobId);
            throw;
        }
    }
}
