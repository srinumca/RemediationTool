using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly string _historyTableName;
    private readonly ILogger<DynamoDbFileFindingRepository> _logger;

    // DynamoDB BatchWriteItem limit — hard AWS limit, never change
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
    // GetAll — full table scan (for small datasets / reporting)
    // -------------------------------------------------------------------------
    public List<FileFinding> GetAll()
    {
        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var request = new ScanRequest
            {
                TableName = _tableName,
                ExclusiveStartKey = lastKey
            };

            var response = _dynamoDb.ScanAsync(request).GetAwaiter().GetResult();

            findings.AddRange(response.Items.Select(DynamoDbAttributeMap.ToFileFinding));

            lastKey = response.LastEvaluatedKey?.Count > 0
                ? response.LastEvaluatedKey
                : null;
        }
        while (lastKey != null);

        return findings;
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
                ["Id"] = new AttributeValue { S = id.ToString() }
            }
        }).GetAwaiter().GetResult();

        return response.Item?.Count > 0
            ? DynamoDbAttributeMap.ToFileFinding(response.Item)
            : null;
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
    // AddRange — BatchWriteItem in chunks of 25 (AWS hard limit)
    // -------------------------------------------------------------------------
    public void AddRange(List<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0) return;

        foreach (var chunk in findings.Chunk(BatchWriteLimit))
        {
            var writeRequests = chunk
                .Select(f => new WriteRequest
                {
                    PutRequest = new PutRequest
                    {
                        Item = DynamoDbAttributeMap.ToMap(f)
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

    // -------------------------------------------------------------------------
    // Update — Option B pattern:
    //   1. Overwrite current state in RemediationFindings (PutItem)
    //   2. Append a new item to FindingHistory (PutItem with ChangedAtUtc SK)
    //
    // The history write is best-effort — failure is logged but does NOT
    // block the current-state update. Current state is always correct.
    // -------------------------------------------------------------------------
    public void Update(FileFinding finding)
    {
        // Step 1 — update current state
        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(finding)
        }).GetAwaiter().GetResult();

        // Step 2 — append to history (best-effort)
        try
        {
            var historyItem = DynamoDbAttributeMap.ToMap(finding);

            // History table PK = SourceRecordId, SK = ChangedAtUtc
            // Use FindingId as fallback when SourceRecordId is null
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
            // History write failure must never block current-state update
            _logger.LogWarning(
                ex,
                "Failed to write finding history. FindingId: {FindingId}. " +
                "Current state updated successfully. History entry missed.",
                finding.Id);
        }
    }
}