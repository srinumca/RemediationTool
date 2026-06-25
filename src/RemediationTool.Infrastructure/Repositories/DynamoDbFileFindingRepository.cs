using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation of IFileFindingRepository.
/// Table: gfr-file-findings-dev
/// Primary key: id (HASH)
/// GSIs:
///   jobId-loadDateUtc-index          → query by reportUid/jobId
///   findingType-loadDateUtc-index    → query by findingType
///   dataSystem-loadDateUtc-index     → query by dataSystem
///   sourceRecordId-loadDateUtc-index → query by sourceRecordId
/// All attribute names are camelCase.
/// </summary>
public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly ILogger<DynamoDbFileFindingRepository> _logger;
    private const int DynamoDbBatchLimit = 25;

    public DynamoDbFileFindingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbFileFindingRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.FindingsTableName;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // WRITE OPERATIONS
    // -------------------------------------------------------------------------

    public void Add(FileFinding finding)
    {
        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(finding)
        }).GetAwaiter().GetResult();
    }

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0) return;

        foreach (var chunk in findings.Chunk(DynamoDbBatchLimit))
        {
            var requests = chunk.Select(f => new WriteRequest
            {
                PutRequest = new PutRequest { Item = DynamoDbAttributeMap.ToMap(f) }
            }).ToList();

            var remaining = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [_tableName] = requests
                }
            };

            var attempts = 0;
            while (true)
            {
                var response = _dynamoDb.BatchWriteItemAsync(remaining).GetAwaiter().GetResult();
                if (!response.UnprocessedItems.Any()) break;
                if (++attempts >= 5)
                {
                    _logger.LogWarning(
                        "Giving up on {Count} unprocessed items after 5 retries.",
                        response.UnprocessedItems.Values.Sum(l => l.Count));
                    break;
                }
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempts)));
                remaining = new BatchWriteItemRequest { RequestItems = response.UnprocessedItems };
            }
        }
    }

    public void Update(FileFinding finding)
    {
        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(finding)
        }).GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // SINGLE RECORD LOOKUPS
    // -------------------------------------------------------------------------

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
        return GetHistoryBySourceRecordId(sourceRecordId)
            .OrderByDescending(f => f.LoadDateUtc)
            .FirstOrDefault();
    }

    // -------------------------------------------------------------------------
    // FILTERED QUERIES — using GSIs
    // -------------------------------------------------------------------------

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "jobId-loadDateUtc-index",
                KeyConditionExpression = "#jobId = :jobId",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#jobId"] = "jobId" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = ingestionJobId }
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }

    public IReadOnlyList<FileFinding> GetLatestByFindingType(FindingType findingType)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "findingType-loadDateUtc-index",
                KeyConditionExpression = "#ft = :ft",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#ft"] = "findingType" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ft"] = new AttributeValue { S = findingType.ToString() }
                },
                ScanIndexForward = false,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "dataSystem-loadDateUtc-index",
                KeyConditionExpression = "#ds = :ds",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#ds"] = "dataSystem" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ds"] = new AttributeValue { S = dataSystem }
                },
                ScanIndexForward = false,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "sourceRecordId-loadDateUtc-index",
                KeyConditionExpression = "#sr = :sr",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#sr"] = "sourceRecordId" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":sr"] = new AttributeValue { S = sourceRecordId }
                },
                ScanIndexForward = true,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }

    // -------------------------------------------------------------------------
    // PAGED QUERY
    // -------------------------------------------------------------------------

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        FindingType? findingType = null)
    {
        Dictionary<string, AttributeValue>? exclusiveStartKey = null;
        if (!string.IsNullOrWhiteSpace(lastEvaluatedKey))
            exclusiveStartKey = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = lastEvaluatedKey }
            };

        ScanResponse response;
        if (findingType.HasValue)
        {
            response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#ft = :ft",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#ft"] = "findingType" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ft"] = new AttributeValue { S = findingType.Value.ToString() }
                },
                Limit = pageSize,
                ExclusiveStartKey = exclusiveStartKey
            }).GetAwaiter().GetResult();
        }
        else
        {
            response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                Limit = pageSize,
                ExclusiveStartKey = exclusiveStartKey
            }).GetAwaiter().GetResult();
        }

        var items = response.Items.Select(DynamoDbAttributeMap.ToFileFinding).ToList();
        var nextPageKey = response.LastEvaluatedKey?.Count > 0
            ? response.LastEvaluatedKey["id"].S
            : null;

        return new PagedResult<FileFinding> { Items = items, NextPageKey = nextPageKey };
    }

    // -------------------------------------------------------------------------
    // AGGREGATE / COUNT
    // -------------------------------------------------------------------------

    public IReadOnlyDictionary<FindingType, int> GetCountByFindingType()
    {
        var counts = new Dictionary<FindingType, int>();
        foreach (FindingType ft in Enum.GetValues<FindingType>())
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#ft = :ft",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#ft"] = "findingType" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ft"] = new AttributeValue { S = ft.ToString() }
                },
                Select = Select.COUNT
            }).GetAwaiter().GetResult();

            counts[ft] = response.Count ?? 0;
        }
        return counts;
    }

    public int CountByFindingType(FindingType findingType)
    {
        var response = _dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "#ft = :ft",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#ft"] = "findingType" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ft"] = new AttributeValue { S = findingType.ToString() }
            },
            Select = Select.COUNT
        }).GetAwaiter().GetResult();

        return response.Count ?? 0;
    }
}