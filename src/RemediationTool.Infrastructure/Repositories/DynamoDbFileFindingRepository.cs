using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly string _historyTableName;
    private readonly ILogger<DynamoDbFileFindingRepository> _logger;

    // DynamoDB hard limit for BatchWriteItem — never change
    private const int BatchWriteLimit = 25;

    public DynamoDbFileFindingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbFileFindingRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.FindingsTableName;
        _historyTableName = options.Value.HistoryTableName;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // GetById — direct primary key lookup O(1)
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

    // -------------------------------------------------------------------------
    // GetLatestBySourceRecordId
    // -------------------------------------------------------------------------
    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        return GetHistoryBySourceRecordId(sourceRecordId)
            .OrderByDescending(f => f.LoadDateUtc)
            .FirstOrDefault();
    }

    // -------------------------------------------------------------------------
    // GetLatestByFindingType — queries GSI: FindingType-LoadDateUtc-index
    // -------------------------------------------------------------------------
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
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#ft"] = "FindingType"
                },
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

    // -------------------------------------------------------------------------
    // GetLatestByDataSystem — queries GSI: DataSystem-LoadDateUtc-index
    // -------------------------------------------------------------------------
    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "DataSystem-LoadDateUtc-index",
                KeyConditionExpression = "DataSystem = :ds",
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

    // -------------------------------------------------------------------------
    // GetHistoryBySourceRecordId — queries GSI: SourceRecordId-LoadDateUtc-index
    // -------------------------------------------------------------------------
    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = "SourceRecordId-LoadDateUtc-index",
                KeyConditionExpression = "#sr = :sr",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#sr"] = "SourceRecordId"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":sr"] = new AttributeValue { S = sourceRecordId }
                },
                ScanIndexForward = true, // oldest first for history
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }

    // -------------------------------------------------------------------------
    // GetByIngestionJobId — queries GSI: IngestionJobId-LoadDateUtc-index
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
                ExpressionAttributeNames = { ["#jobId"] = "jobId" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jid"] = new AttributeValue { S = ingestionJobId }
                },
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }

    // -------------------------------------------------------------------------
    // GetLatestPaged — cursor-based scan with optional FindingType filter
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
                ["Id"] = new AttributeValue { S = lastEvaluatedKey }
            };

        ScanResponse response;

        if (findingType.HasValue)
        {
            response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "FindingType = :ft",
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

        var items = response.Items
            .Select(DynamoDbAttributeMap.ToFileFinding)
            .ToList();

        var nextPageKey = response.LastEvaluatedKey?.Count > 0
            ? response.LastEvaluatedKey["Id"].S
            : null;

        return new PagedResult<FileFinding>
        {
            Items = items,
            NextPageKey = nextPageKey
        };
    }

    // -------------------------------------------------------------------------
    // GetCountByFindingType — dashboard KPI counts per type
    // -------------------------------------------------------------------------
    public IReadOnlyDictionary<FindingType, int> GetCountByFindingType()
    {
        var counts = new Dictionary<FindingType, int>();

        foreach (FindingType ft in Enum.GetValues<FindingType>())
        {
            var response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "FindingType = :ft",
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

    // -------------------------------------------------------------------------
    // CountByFindingType — count for a single type
    // -------------------------------------------------------------------------
    public int CountByFindingType(FindingType findingType)
    {
        var response = _dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "FindingType = :ft",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ft"] = new AttributeValue { S = findingType.ToString() }
            },
            Select = Select.COUNT
        }).GetAwaiter().GetResult();

        return response.Count ?? 0;
    }

    // -------------------------------------------------------------------------
    // Add — single PutItem
    // -------------------------------------------------------------------------
    public void Add(FileFinding finding)
    {
        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(finding)
        }).GetAwaiter().GetResult();
    }

    // -------------------------------------------------------------------------
    // AddRange — BatchWriteItem in chunks of 25 with unprocessed-item retry
    // Pattern mirrors AwsSampleApi/Features/CsvProcessing/CsvProcessingService.cs
    // -------------------------------------------------------------------------
    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0) return;

        foreach (var chunk in findings.Chunk(BatchWriteLimit))
        {
            var writeRequests = chunk
                .Select(f => new WriteRequest
                {
                    PutRequest = new PutRequest { Item = DynamoDbAttributeMap.ToMap(f) }
                })
                .ToList();

            var remaining = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [_tableName] = writeRequests
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
                        "Giving up on {Count} unprocessed items after 5 retries. TableName: {Table}",
                        response.UnprocessedItems.Values.Sum(l => l.Count),
                        _tableName);
                    break;
                }

                // Exponential backoff — same pattern as AwsSampleApi
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempts)));
                remaining = new BatchWriteItemRequest { RequestItems = response.UnprocessedItems };
            }
        }
    }

    // -------------------------------------------------------------------------
    // Update — Option B:
    //   1. Overwrite current state in RemediationFindings (PutItem)
    //   2. Append new item to FindingHistory (best-effort — never blocks main write)
    // -------------------------------------------------------------------------
    public void Update(FileFinding finding)
    {
        // Step 1 — overwrite current state
        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(finding)
        }).GetAwaiter().GetResult();

        // Step 2 — append to history (best-effort)
        try
        {
            var historyItem = DynamoDbAttributeMap.ToMap(finding);
            var sourceRecordId = finding.SourceRecordId ?? finding.Id.ToString();
            historyItem["SourceRecordId"] = new AttributeValue { S = sourceRecordId };
            historyItem["ChangedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
            historyItem["FindingId"] = new AttributeValue { S = finding.Id.ToString() };
            historyItem["HistoryId"] = new AttributeValue { S = Guid.NewGuid().ToString() };

            _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _historyTableName,
                Item = historyItem
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to write FindingHistory (best-effort). FindingId: {FindingId}. " +
                "Current state updated successfully.",
                finding.Id);
        }
    }
}