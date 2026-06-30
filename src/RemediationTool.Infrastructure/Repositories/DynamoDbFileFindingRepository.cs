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

    /// <summary>
    /// Adds a new FileFinding record to the DynamoDB table.
    /// </summary>
    /// <param name="finding"></param>
    public void Add(FileFinding finding)
    {
        _logger.LogDebug("Adding FileFinding to DynamoDB. Id: {Id}, FileName: {FileName}", finding.Id, finding.FindingFileName);
        try
        {
            _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item = DynamoDbAttributeMap.ToMap(finding)
            }).GetAwaiter().GetResult();
            _logger.LogDebug("FileFinding added successfully. Id: {Id}", finding.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding FileFinding to DynamoDB. Id: {Id}", finding.Id);
            throw;
        }
    }


    /// <summary>
    /// Adds a range of FileFinding records to the DynamoDB table in batches.
    /// </summary>
    /// <param name="findings"></param>
    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        _logger.LogInformation("Adding {Count} FileFinding records to DynamoDB", findings?.Count ?? 0);
        if (findings == null || findings.Count == 0)
        {
            _logger.LogDebug("AddRange called with empty findings list");
            return;
        }

        try
        {
            foreach (var chunk in findings.Chunk(BatchLimit))
            {
                _logger.LogDebug("Processing batch of {Count} records", chunk.Length);
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
                    _logger.LogDebug("Batch write retry {AttemptNumber}. UnprocessedItems: {Count}", attempts, resp.UnprocessedItems.Values.Sum(l => l.Count));
                    Thread.Sleep(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempts)));
                    remaining = new BatchWriteItemRequest { RequestItems = resp.UnprocessedItems };
                }
            }
            _logger.LogInformation("Successfully added {Count} FileFinding records to DynamoDB", findings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding range of FileFinding records to DynamoDB. Count: {Count}", findings.Count);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing FileFinding record in the DynamoDB table.
    /// </summary>
    /// <param name="finding"></param>
    public void Update(FileFinding finding)
    {
        _logger.LogDebug("Updating FileFinding in DynamoDB. Id: {Id}, FileName: {FileName}", finding.Id, finding.FindingFileName);
        try
        {
            _dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item = DynamoDbAttributeMap.ToMap(finding)
            }).GetAwaiter().GetResult();
            _logger.LogDebug("FileFinding updated successfully. Id: {Id}", finding.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating FileFinding in DynamoDB. Id: {Id}", finding.Id);
            throw;
        }
    }

    // ── Single-record lookups ─────────────────────────────────────────────────

    /// <summary>
    /// Gets a FileFinding record by its unique Id from the DynamoDB table.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public FileFinding? GetById(Guid id)
    {
        _logger.LogDebug("Getting FileFinding by Id: {Id}", id);
        try
        {
            var response = _dynamoDb.GetItemAsync(new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = id.ToString() }
                }
            }).GetAwaiter().GetResult();

            var result = response.Item?.Count > 0
                ? DynamoDbAttributeMap.ToFileFinding(response.Item)
                : null;

            if (result != null)
                _logger.LogDebug("FileFinding found. Id: {Id}, FileName: {FileName}", id, result.FindingFileName);
            else
                _logger.LogDebug("FileFinding not found. Id: {Id}", id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting FileFinding by Id. Id: {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Gets the latest FileFinding record by its SourceRecordId from the DynamoDB table.
    /// </summary>
    /// <param name="sourceRecordId"></param>
    /// <returns></returns>
    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        _logger.LogDebug("Getting latest FileFinding by SourceRecordId: {SourceRecordId}", sourceRecordId);
        var result = GetHistoryBySourceRecordId(sourceRecordId)
            .OrderByDescending(f => f.LoadDateUtc)
            .FirstOrDefault();
        if (result != null)
            _logger.LogDebug("Latest FileFinding found. SourceRecordId: {SourceRecordId}, FileName: {FileName}", sourceRecordId, result.FindingFileName);
        else
            _logger.LogDebug("No FileFinding found. SourceRecordId: {SourceRecordId}", sourceRecordId);
        return result;
    }

    // ── Filtered queries ──────────────────────────────────────────────────────

    /// <summary>
    /// Gets all FileFinding records associated with a specific IngestionJobId from the DynamoDB table.
    /// </summary>
    /// <param name="ingestionJobId"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        _logger.LogDebug("Getting FileFinding records by IngestionJobId: {IngestionJobId}", ingestionJobId);
        var results = QueryGsi("jobId-loadDateUtc-index", "#jobId", "jobId", ingestionJobId);
        _logger.LogInformation("Retrieved {Count} FileFinding records for IngestionJobId: {IngestionJobId}", results.Count, ingestionJobId);
        return results;
    }

    /// <summary>
    /// Gets all FileFinding records associated with a specific FindingType from the DynamoDB table.
    /// </summary>
    /// <param name="findingType"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
    {
        _logger.LogDebug("Getting FileFinding records by FindingType: {FindingType}", findingType);
        var results = QueryGsi("findingType-loadDateUtc-index", "#ft", "findingType", findingType);
        _logger.LogInformation("Retrieved {Count} FileFinding records for FindingType: {FindingType}", results.Count, findingType);
        return results;
    }

    /// <summary>
    /// Gets all FileFinding records associated with a specific DataSystem from the DynamoDB table.
    /// </summary>
    /// <param name="dataSystem"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
        => QueryGsi("dataSystem-loadDateUtc-index", "#ds", "dataSystem", dataSystem);

    /// <summary>
    /// Gets the history of FileFinding records associated with a specific SourceRecordId from the DynamoDB table.
    /// </summary>
    /// <param name="sourceRecordId"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
        => QueryGsi("sourceRecordId-loadDateUtc-index", "#sr", "sourceRecordId",
                    sourceRecordId, scanIndexForward: true);

    /// <summary>
    /// Gets all FileFinding records from the DynamoDB table. This method performs a full table scan and may be inefficient for large datasets.
    /// </summary>
    /// <returns></returns>
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

    /// <summary>
    /// Gets a paginated list of the latest FileFinding records from the DynamoDB table, optionally filtered by FindingType. This method uses a Scan operation and may be inefficient for large datasets.
    /// </summary>
    /// <param name="pageSize"></param>
    /// <param name="lastEvaluatedKey"></param>
    /// <param name="findingType"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Gets a dictionary containing the count of FileFinding records grouped by FindingType. This method performs a full table scan and may be inefficient for large datasets.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyDictionary<string, int> GetCountByFindingType()
    {
        var counts = new Dictionary<string, int>();
        var all = GetAll();
        foreach (var grp in all.GroupBy(f => f.FindingType))
            counts[grp.Key] = grp.Count();
        return counts;
    }

    /// <summary>
    /// Gets the count of FileFinding records associated with a specific FindingType from the DynamoDB table.
    /// </summary>
    /// <param name="findingType"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Queries a Global Secondary Index (GSI) in the DynamoDB table based on the provided index name, attribute names, and value. This method handles pagination and retrieves all matching records.
    /// </summary>
    /// <param name="indexName"></param>
    /// <param name="exprAttrName"></param>
    /// <param name="attrName"></param>
    /// <param name="value"></param>
    /// <param name="scanIndexForward"></param>
    /// <returns></returns>
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