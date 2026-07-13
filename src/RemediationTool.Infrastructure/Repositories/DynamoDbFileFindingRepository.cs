using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation of IFileFindingRepository.
/// </summary>
public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private const int BatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly ILogger<DynamoDbFileFindingRepository> _logger;

    public DynamoDbFileFindingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbFileFindingRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.FindingsTableName;
        _logger = logger;
    }

    public void Add(FileFinding finding)
        => Put(finding);

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0)
            return;

        var chunkNumber = 0;
        foreach (var chunk in findings.Chunk(BatchLimit))
        {
            chunkNumber++;
            var requests = new List<WriteRequest>(chunk.Length);

            foreach (var finding in chunk)
            {
                requests.Add(new WriteRequest
                {
                    PutRequest = new PutRequest
                    {
                        Item = DynamoDbAttributeMap.ToMap(finding)
                    }
                });
            }

            ExecuteBatchWriteWithRetry(
                requests,
                operationName: "FindingsBatchWrite",
                chunkNumber,
                totalInputCount: findings.Count);
        }
    }

    public void Update(FileFinding finding)
        => Put(finding);

    public FileFinding? GetById(Guid id)
    {
        var response = _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = id.ToString() }
            }
        }).GetAwaiter().GetResult();

        return response.Item?.Count > 0
            ? DynamoDbAttributeMap.ToFileFinding(response.Item)
            : null;
    }

    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        if (string.IsNullOrWhiteSpace(sourceRecordId))
            return null;

        var response = _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            IndexName = "sourceRecordId-loadDateUtc-index",
            KeyConditionExpression = "#sr = :val",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#sr"] = "sourceRecordId"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":val"] = new AttributeValue { S = sourceRecordId }
            },
            ScanIndexForward = false,
            Limit = 1
        }).GetAwaiter().GetResult();

        return response.Items.Count == 0
            ? null
            : DynamoDbAttributeMap.ToFileFinding(response.Items[0]);
    }

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
        => QueryGsi("jobId-loadDateUtc-index", "#jobId", "jobId", ingestionJobId);

    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
    {
        try
        {
            return QueryGsi(
                "findingType-loadDateUtc-index",
                "#ft",
                "findingType",
                findingType);
        }
        catch (Exception ex) when (IsMissingIndexException(ex))
        {
            _logger.LogWarning(
                ex,
                "[DYNAMODB_GSI_MISSING_FALLBACK] Table:{Table}, Index:{IndexName}, FindingType:{FindingType}. Falling back to filtered scan.",
                _tableName,
                "findingType-loadDateUtc-index",
                findingType);

            return ScanByAttribute("findingType", findingType);
        }
    }

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
        => QueryGsi("dataSystem-loadDateUtc-index", "#ds", "dataSystem", dataSystem);

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
        => QueryGsi(
            "sourceRecordId-loadDateUtc-index",
            "#sr",
            "sourceRecordId",
            sourceRecordId,
            scanIndexForward: true);

    public List<FileFinding> GetAll()
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            AddMappedFindings(findings, response.Items);
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return findings;
    }

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        string? findingType = null)
    {
        Dictionary<string, AttributeValue>? startKey = null;
        if (!string.IsNullOrWhiteSpace(lastEvaluatedKey))
        {
            startKey = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = lastEvaluatedKey }
            };
        }

        var request = new ScanRequest
        {
            TableName = _tableName,
            Limit = pageSize,
            ExclusiveStartKey = startKey
        };

        if (!string.IsNullOrWhiteSpace(findingType))
        {
            request.FilterExpression = "#ft = :ft";
            request.ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#ft"] = "findingType"
            };
            request.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ft"] = new AttributeValue { S = findingType }
            };
        }

        var response = _dynamoDb.ScanAsync(request).GetAwaiter().GetResult();
        var items = new List<FileFinding>(response.Items.Count);
        AddMappedFindings(items, response.Items);

        var nextPageKey = response.LastEvaluatedKey?.Count > 0
            && response.LastEvaluatedKey.TryGetValue("id", out var idValue)
                ? idValue.S
                : null;

        return new PagedResult<FileFinding>
        {
            Items = items,
            NextPageKey = nextPageKey
        };
    }

    public IReadOnlyDictionary<string, int> GetCountByFindingType()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                ProjectionExpression = "#ft",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#ft"] = "findingType"
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            foreach (var item in response.Items)
            {
                var findingType = item.TryGetValue("findingType", out var value)
                    ? value.S ?? string.Empty
                    : string.Empty;

                if (counts.TryGetValue(findingType, out var currentCount))
                    counts[findingType] = currentCount + 1;
                else
                    counts[findingType] = 1;
            }

            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return counts;
    }

    public int CountByFindingType(string findingType)
    {
        if (string.IsNullOrWhiteSpace(findingType))
            return 0;

        try
        {
            return CountByGsi(
                "findingType-loadDateUtc-index",
                "#ft",
                "findingType",
                findingType);
        }
        catch (Exception ex) when (IsMissingIndexException(ex))
        {
            _logger.LogWarning(
                ex,
                "[DYNAMODB_GSI_MISSING_COUNT_FALLBACK] Table:{Table}, Index:{IndexName}, FindingType:{FindingType}. Falling back to paged count scan.",
                _tableName,
                "findingType-loadDateUtc-index",
                findingType);

            return CountByAttributeScan("findingType", findingType);
        }
    }

    private void Put(FileFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(finding)
        }).GetAwaiter().GetResult();
    }

    private IReadOnlyList<FileFinding> ScanByAttribute(string attributeName, string value)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#attr = :val",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#attr"] = attributeName
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { S = value }
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            AddMappedFindings(findings, response.Items);
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return findings;
    }

    private int CountByGsi(
        string indexName,
        string expressionAttributeName,
        string attributeName,
        string value)
    {
        var count = 0;
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = indexName,
                KeyConditionExpression = $"{expressionAttributeName} = :val",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    [expressionAttributeName] = attributeName
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { S = value }
                },
                Select = Select.COUNT,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            count += response.Count ?? 0;
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return count;
    }

    private int CountByAttributeScan(string attributeName, string value)
    {
        var count = 0;
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#attr = :val",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#attr"] = attributeName
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { S = value }
                },
                Select = Select.COUNT,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            count += response.Count ?? 0;
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return count;
    }

    private void ExecuteBatchWriteWithRetry(
        List<WriteRequest> writeRequests,
        string operationName,
        int chunkNumber,
        int totalInputCount)
    {
        var remaining = new BatchWriteItemRequest
        {
            RequestItems = new Dictionary<string, List<WriteRequest>>
            {
                [_tableName] = writeRequests
            }
        };

        var retryAttempt = 0;
        while (true)
        {
            var response = _dynamoDb.BatchWriteItemAsync(remaining).GetAwaiter().GetResult();
            if (response.UnprocessedItems == null || response.UnprocessedItems.Count == 0)
            {
                _logger.LogInformation(
                    "[DYNAMODB_BATCH_WRITE_COMPLETE] Operation:{Operation}, Table:{Table}, ChunkNumber:{ChunkNumber}, ChunkSize:{ChunkSize}, TotalInputCount:{TotalInputCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    _tableName,
                    chunkNumber,
                    writeRequests.Count,
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
                _logger.LogError(
                    "[DYNAMODB_BATCH_WRITE_UNPROCESSED_EXHAUSTED] Operation:{Operation}, Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    _tableName,
                    chunkNumber,
                    unprocessedCount,
                    retryAttempt);

                throw new InvalidOperationException(
                    $"{operationName} failed for table {_tableName}. {unprocessedCount} item(s) remained unprocessed after {retryAttempt} retry attempt(s).");
            }

            var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt));
            _logger.LogWarning(
                "[DYNAMODB_BATCH_WRITE_UNPROCESSED_RETRY] Operation:{Operation}, Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempt:{RetryAttempt}, DelayMs:{DelayMs}",
                operationName,
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

    private IReadOnlyList<FileFinding> QueryGsi(
        string indexName,
        string expressionAttributeName,
        string attributeName,
        string value,
        bool scanIndexForward = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<FileFinding>();

        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = indexName,
                KeyConditionExpression = $"{expressionAttributeName} = :val",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    [expressionAttributeName] = attributeName
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { S = value }
                },
                ScanIndexForward = scanIndexForward,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            AddMappedFindings(findings, response.Items);
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return findings;
    }

    private static void AddMappedFindings(
        List<FileFinding> destination,
        List<Dictionary<string, AttributeValue>> items)
    {
        if (items.Count == 0)
            return;

        destination.EnsureCapacity(destination.Count + items.Count);
        foreach (var item in items)
            destination.Add(DynamoDbAttributeMap.ToFileFinding(item));
    }

    private static Dictionary<string, AttributeValue>? GetNextKey(
        Dictionary<string, AttributeValue>? lastEvaluatedKey)
        => lastEvaluatedKey?.Count > 0 ? lastEvaluatedKey : null;

    private static bool IsMissingIndexException(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("does not have the specified index", StringComparison.OrdinalIgnoreCase)
               || message.Contains("specified index", StringComparison.OrdinalIgnoreCase);
    }
}
