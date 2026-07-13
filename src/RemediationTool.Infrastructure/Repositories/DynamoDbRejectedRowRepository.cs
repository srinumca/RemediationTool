using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation of IRejectedRowRepository.
/// </summary>
public class DynamoDbRejectedRowRepository : IRejectedRowRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly ILogger<DynamoDbRejectedRowRepository> _logger;

    public DynamoDbRejectedRowRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbRejectedRowRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.RejectedRowsTableName;
        _logger = logger;
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

            AddMappedRows(rows, response.Items);
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return rows;
    }

    public List<RejectedRowDetail> GetByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new List<RejectedRowDetail>();

        var rows = new List<RejectedRowDetail>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "uid-rowCreatedDateOn-index",
                KeyConditionExpression = "#uid = :uid",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#uid"] = "uid"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":uid"] = new AttributeValue { S = jobId }
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            AddMappedRows(rows, response.Items);
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return rows;
    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0)
            return;

        var chunkNumber = 0;
        foreach (var chunk in rejectedRows.Chunk(DynamoDbBatchLimit))
        {
            chunkNumber++;
            var requests = new List<WriteRequest>(chunk.Length);

            foreach (var rejectedRow in chunk)
            {
                requests.Add(new WriteRequest
                {
                    PutRequest = new PutRequest
                    {
                        Item = DynamoDbAttributeMap.ToMap(rejectedRow)
                    }
                });
            }

            ExecuteBatchWriteWithRetry(requests, chunkNumber, rejectedRows.Count);
        }
    }

    private void ExecuteBatchWriteWithRetry(
        List<WriteRequest> requests,
        int chunkNumber,
        int totalInputCount)
    {
        var remaining = new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, List<WriteRequest>>
            {
                [_tableName] = requests
            }
        };

        var retryAttempt = 0;
        while (true)
        {
            var response = _dynamoDb.BatchWriteItemAsync(remaining).GetAwaiter().GetResult();
            if (response.UnprocessedItems == null || response.UnprocessedItems.Count == 0)
            {
                _logger.LogInformation(
                    "[REJECTED_ROWS_BATCH_WRITE_COMPLETE] Table:{Table}, ChunkNumber:{ChunkNumber}, ChunkSize:{ChunkSize}, TotalInputCount:{TotalInputCount}, RetryAttempts:{RetryAttempts}",
                    _tableName,
                    chunkNumber,
                    requests.Count,
                    totalInputCount,
                    retryAttempt);
                return;
            }

            var unprocessedCount = 0;
            foreach (var items in response.UnprocessedItems.Values)
                unprocessedCount += items.Count;

            retryAttempt++;
            if (retryAttempt >= MaxUnprocessedItemRetryAttempts)
            {
                throw new InvalidOperationException(
                    $"Rejected-row batch write failed for table {_tableName}. {unprocessedCount} item(s) remained unprocessed after {retryAttempt} retry attempt(s).");
            }

            var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt));
            _logger.LogWarning(
                "[REJECTED_ROWS_BATCH_WRITE_RETRY] Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempt:{RetryAttempt}, DelayMs:{DelayMs}",
                _tableName,
                chunkNumber,
                unprocessedCount,
                retryAttempt,
                delay.TotalMilliseconds);

            Thread.Sleep(delay);
            remaining = new BatchWriteItemRequest
            {
                RequestItems = response.UnprocessedItems
            };
        }
    }

    private static void AddMappedRows(
        List<RejectedRowDetail> destination,
        List<Dictionary<string, AttributeValue>> items)
    {
        if (items.Count == 0)
            return;

        destination.EnsureCapacity(destination.Count + items.Count);
        foreach (var item in items)
            destination.Add(DynamoDbAttributeMap.ToRejectedRowDetail(item));
    }

    private static Dictionary<string, AttributeValue>? GetNextKey(
        Dictionary<string, AttributeValue>? lastEvaluatedKey)
        => lastEvaluatedKey?.Count > 0 ? lastEvaluatedKey : null;
}
