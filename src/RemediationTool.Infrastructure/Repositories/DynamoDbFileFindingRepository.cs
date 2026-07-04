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
/// Table: gfr-file-findings-dev
/// Primary key: id (HASH) — camelCase
/// GSIs (all camelCase):
///   jobId-loadDateUtc-index
///   findingType-loadDateUtc-index
///   dataSystem-loadDateUtc-index
///   sourceRecordId-loadDateUtc-index
/// </summary>
public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly ILogger<DynamoDbFileFindingRepository> _logger;
    private const int BatchLimit = 25;

    public DynamoDbFileFindingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbFileFindingRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.FindingsTableName;
        _logger = logger;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

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

        foreach (var chunk in findings.Chunk(BatchLimit))
        {
            var requests = chunk.Select(f => new WriteRequest
            {
                PutRequest = new PutRequest { Item = DynamoDbAttributeMap.ToMap(f) }
            }).ToList();

            var remaining = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                { [_tableName] = requests }
            };

            var attempts = 0;
            while (true)
            {
                var resp = _dynamoDb.BatchWriteItemAsync(remaining).GetAwaiter().GetResult();
                if (!resp.UnprocessedItems.Any()) break;
                if (++attempts >= 5)
                {
                    _logger.LogWarning(
                        "Giving up on {Count} unprocessed items after 5 retries.",
                        resp.UnprocessedItems.Values.Sum(l => l.Count));
                    break;
                }
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempts)));
                remaining = new BatchWriteItemRequest { RequestItems = resp.UnprocessedItems };
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

    // ── Single-record lookups ─────────────────────────────────────────────────

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
        => GetHistoryBySourceRecordId(sourceRecordId)
            .OrderByDescending(f => f.LoadDateUtc)
            .FirstOrDefault();

    // ── Filtered queries ──────────────────────────────────────────────────────

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
        => QueryGsi("jobId-loadDateUtc-index", "#jobId", "jobId", ingestionJobId);

    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
        => QueryGsi("findingType-loadDateUtc-index", "#ft", "findingType", findingType);

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
        => QueryGsi("dataSystem-loadDateUtc-index", "#ds", "dataSystem", dataSystem);

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
        => QueryGsi("sourceRecordId-loadDateUtc-index", "#sr", "sourceRecordId",
                    sourceRecordId, scanIndexForward: true);

    // ── GetAll (legacy — used by ReportService) ───────────────────────────────

    public List<FileFinding> GetAll()
    {
        var all = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;
        do
        {
            var resp = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            all.AddRange(resp.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = resp.LastEvaluatedKey?.Count > 0 ? resp.LastEvaluatedKey : null;
        }
        while (lastKey != null);
        return all;
    }

    // ── Paged query ───────────────────────────────────────────────────────────

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        string? findingType = null)
    {
        Dictionary<string, AttributeValue>? startKey = null;
        if (!string.IsNullOrWhiteSpace(lastEvaluatedKey))
            startKey = new Dictionary<string, AttributeValue>
            { ["id"] = new AttributeValue { S = lastEvaluatedKey } };

        ScanResponse response;
        if (!string.IsNullOrWhiteSpace(findingType))
        {
            response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#ft = :ft",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#ft"] = "findingType" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                { [":ft"] = new AttributeValue { S = findingType } },
                Limit = pageSize,
                ExclusiveStartKey = startKey
            }).GetAwaiter().GetResult();
        }
        else
        {
            response = _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = _tableName,
                Limit = pageSize,
                ExclusiveStartKey = startKey
            }).GetAwaiter().GetResult();
        }

        var items = response.Items.Select(DynamoDbAttributeMap.ToFileFinding).ToList();
        var nextKey = response.LastEvaluatedKey?.Count > 0
            ? response.LastEvaluatedKey["id"].S : null;

        return new PagedResult<FileFinding> { Items = items, NextPageKey = nextKey };
    }

    // ── Aggregates ────────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, int> GetCountByFindingType()
    {
        var counts = new Dictionary<string, int>();
        var all = GetAll();
        foreach (var grp in all.GroupBy(f => f.FindingType))
            counts[grp.Key] = grp.Count();
        return counts;
    }

    public int CountByFindingType(string findingType)
    {
        var response = _dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "#ft = :ft",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#ft"] = "findingType" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            { [":ft"] = new AttributeValue { S = findingType } },
            Select = Select.COUNT
        }).GetAwaiter().GetResult();

        return response.Count ?? 0;
    }

    // ── Private helper ────────────────────────────────────────────────────────

    private IReadOnlyList<FileFinding> QueryGsi(
        string indexName, string exprAttrName, string attrName,
        string value, bool scanIndexForward = false)
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var resp = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                IndexName = indexName,
                KeyConditionExpression = $"{exprAttrName} = :val",
                ExpressionAttributeNames = new Dictionary<string, string> { [exprAttrName] = attrName },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                { [":val"] = new AttributeValue { S = value } },
                ScanIndexForward = scanIndexForward,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.AddRange(resp.Items.Select(DynamoDbAttributeMap.ToFileFinding));
            lastKey = resp.LastEvaluatedKey?.Count > 0 ? resp.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        return findings;
    }
}