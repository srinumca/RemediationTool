using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbRejectedRowRepository : IRejectedRowRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    private const int BatchWriteLimit = 25;

    public DynamoDbRejectedRowRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.RejectedRowsTableName;
    }

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

            lastKey = response.LastEvaluatedKey?.Count > 0
                ? response.LastEvaluatedKey
                : null;
        }
        while (lastKey != null);

        return rows;
    }

    public List<RejectedRowDetail> GetByJobId(string jobId)
    {
        // Query the GSI: JobId-ErrorDateUtc-index
        var rows = new List<RejectedRowDetail>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "JobId-ErrorDateUtc-index",
                KeyConditionExpression = "JobId = :jobId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = jobId }
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            rows.AddRange(response.Items.Select(DynamoDbAttributeMap.ToRejectedRowDetail));

            lastKey = response.LastEvaluatedKey?.Count > 0
                ? response.LastEvaluatedKey
                : null;
        }
        while (lastKey != null);

        return rows;
    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0) return;

        foreach (var chunk in rejectedRows.Chunk(BatchWriteLimit))
        {
            var writeRequests = chunk
                .Select(r => new WriteRequest
                {
                    PutRequest = new PutRequest
                    {
                        Item = DynamoDbAttributeMap.ToMap(r)
                    }
                })
                .ToList();

            _dynamoDb.BatchWriteItemAsync(new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [_tableName] = writeRequests
                }
            }).GetAwaiter().GetResult();
        }
    }
}