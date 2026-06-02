using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbIngestionStagingRepository : IIngestionStagingRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    private const int BatchWriteLimit = 25;

    // TTL — staged records auto-expire after 7 days if never cleaned up
    // (handles server crash mid-resume scenario)
    private static readonly TimeSpan TtlDuration = TimeSpan.FromDays(7);

    public DynamoDbIngestionStagingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.StagedFindingsTableName;
    }

    // -------------------------------------------------------------------------
    // SaveValidFindings
    // Replaces any existing staged records for this JobId (idempotent on re-upload)
    // -------------------------------------------------------------------------
    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0) return;

        // Delete any existing staged records for this jobId first
        DeleteByJobId(jobId);

        // Write all new records in batches of 25
        var expiresAt = DateTimeOffset.UtcNow.Add(TtlDuration).ToUnixTimeSeconds();
        var sequenceNumber = 1;

        foreach (var chunk in validFindings.Chunk(BatchWriteLimit))
        {
            var writeRequests = new List<WriteRequest>();

            foreach (var finding in chunk)
            {
                var item = new Dictionary<string, AttributeValue>
                {
                    // Composite key: JobId (PK) + SequenceNumber (SK)
                    ["JobId"] = new AttributeValue { S = jobId },
                    ["SequenceNumber"] = new AttributeValue { N = sequenceNumber.ToString() },
                    ["CreatedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") },
                    ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() }, // TTL

                    // Store the full FileFinding as a nested DynamoDB Map
                    ["Finding"] = new AttributeValue { M = DynamoDbAttributeMap.ToMap(finding) }
                };

                writeRequests.Add(new WriteRequest
                {
                    PutRequest = new PutRequest { Item = item }
                });

                sequenceNumber++;
            }

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
    // GetValidFindingsAfter
    // Returns records with SequenceNumber > lastProcessedRecordCount
    // Uses Query on composite key (JobId + SequenceNumber range condition)
    // -------------------------------------------------------------------------
    public List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return new List<FileFinding>();

        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "JobId = :jobId AND SequenceNumber > :lastSeq",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = jobId },
                    [":lastSeq"] = new AttributeValue { N = lastProcessedRecordCount.ToString() }
                },
                ScanIndexForward = true,   // ascending SequenceNumber order
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            foreach (var item in response.Items)
            {
                if (item.TryGetValue("Finding", out var findingAttr) && findingAttr.M != null)
                    findings.Add(DynamoDbAttributeMap.ToFileFinding(findingAttr.M));
            }

            lastKey = response.LastEvaluatedKey?.Count > 0
                ? response.LastEvaluatedKey
                : null;
        }
        while (lastKey != null);

        return findings;
    }

    // -------------------------------------------------------------------------
    // CountByJobId
    // -------------------------------------------------------------------------
    public int CountByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return 0;

        var response = _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "JobId = :jobId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":jobId"] = new AttributeValue { S = jobId }
            },
            Select = Select.COUNT
        }).GetAwaiter().GetResult();

        return response.Count;
    }

    // -------------------------------------------------------------------------
    // DeleteByJobId
    // Removes all staged records for the job after successful completion
    // -------------------------------------------------------------------------
    public void DeleteByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return;

        // Query all keys first (can't batch delete without knowing the keys)
        var keysToDelete = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "JobId = :jobId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = jobId }
                },
                // Only fetch the key attributes — no need to load Finding data
                ProjectionExpression = "JobId, SequenceNumber",
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            keysToDelete.AddRange(response.Items);

            lastKey = response.LastEvaluatedKey?.Count > 0
                ? response.LastEvaluatedKey
                : null;
        }
        while (lastKey != null);

        if (keysToDelete.Count == 0) return;

        // Delete in batches of 25
        foreach (var chunk in keysToDelete.Chunk(BatchWriteLimit))
        {
            var deleteRequests = chunk
                .Select(key => new WriteRequest
                {
                    DeleteRequest = new DeleteRequest { Key = key }
                })
                .ToList();

            _dynamoDb.BatchWriteItemAsync(new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    [_tableName] = deleteRequests
                }
            }).GetAwaiter().GetResult();
        }
    }
}