using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation of IRejectedRowRepository.
/// Table: gfr-rejected-rows-dev
/// Primary key: rejectedRowId (HASH)
/// GSI: jobId-errorDateUtc-index → query all rejections for a job
/// All attribute names are camelCase.
/// </summary>
public class DynamoDbRejectedRowRepository : IRejectedRowRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private const int DynamoDbBatchLimit = 25;

    public DynamoDbRejectedRowRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.RejectedRowsTableName;
    }

    /// <summary>
    /// Retrieves all rejected rows from DynamoDB.
    /// </summary>
    /// <returns></returns>
    public List<RejectedRowDetail> GetAll()
    {
        var rows = new List<RejectedRowDetail>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            rows.AddRange(response.Items.Select(DynamoDbAttributeMap.ToRejectedRowDetail));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return rows;
    }

    /// <summary>
    /// Retrieves all rejected rows for a specific jobId from DynamoDB using the GSI.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    public List<RejectedRowDetail> GetByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return new List<RejectedRowDetail>();

        var rows = new List<RejectedRowDetail>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "jobId-errorDateUtc-index",
                KeyConditionExpression = "#jobId = :jobId",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#jobId"] = "jobId" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = jobId }
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            rows.AddRange(response.Items.Select(DynamoDbAttributeMap.ToRejectedRowDetail));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return rows;
    }

    /// <summary>
    /// Adds a list of rejected rows to DynamoDB in batches, respecting the DynamoDB batch write limit.
    /// </summary>
    /// <param name="rejectedRows"></param>
    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0) return;

        foreach (var chunk in rejectedRows.Chunk(DynamoDbBatchLimit))
        {
            var requests = chunk.Select(r => new WriteRequest
            {
                PutRequest = new PutRequest { Item = DynamoDbAttributeMap.ToMap(r) }
            }).ToList();

            _dynamoDb.BatchWriteItemAsync(new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [_tableName] = requests
                }
            }).GetAwaiter().GetResult();
        }
    }
}