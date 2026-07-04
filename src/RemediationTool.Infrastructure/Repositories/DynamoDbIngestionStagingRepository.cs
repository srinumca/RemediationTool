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

    // Staged records auto-expire after 7 days if never cleaned up (server crash scenario)

    private static readonly TimeSpan TtlDuration = TimeSpan.FromDays(7);

    public DynamoDbIngestionStagingRepository(

        IAmazonDynamoDB dynamoDb,

        IOptions<DynamoDbOptions> options)

    {

        _dynamoDb = dynamoDb;

        _tableName = options.Value.StagedFindingsTableName;

    }

    // -------------------------------------------------------------------------

    // SaveValidFindings — idempotent (delete existing for jobId, write new)

    // -------------------------------------------------------------------------

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)

    {

        if (string.IsNullOrWhiteSpace(jobId))

            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0) return;

        // Delete any existing staged records for this jobId first (idempotent on re-upload)

        DeleteByJobId(jobId);

        var expiresAt = DateTimeOffset.UtcNow.Add(TtlDuration).ToUnixTimeSeconds();

        var sequenceNumber = 1;

        foreach (var chunk in validFindings.Chunk(BatchWriteLimit))

        {

            var writeRequests = new List<WriteRequest>();

            foreach (var finding in chunk)

            {

                var item = new Dictionary<string, AttributeValue>

                {

                    ["jobId"] = new AttributeValue { S = jobId },

                    ["sequenceNumber"] = new AttributeValue { N = sequenceNumber.ToString() },

                    ["CreatedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") },

                    ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() },  // TTL

                    ["finding"] = new AttributeValue { M = DynamoDbAttributeMap.ToMap(finding) }

                };

                writeRequests.Add(new WriteRequest { PutRequest = new PutRequest { Item = item } });

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

    // GetValidFindingsAfter — resume path, skips already-processed records

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

                KeyConditionExpression = "jobId = :jobId AND sequenceNumber > :lastSeq",

                ExpressionAttributeValues = new Dictionary<string, AttributeValue>

                {

                    [":jobId"] = new AttributeValue { S = jobId },

                    [":lastSeq"] = new AttributeValue { N = lastProcessedRecordCount.ToString() }

                },

                ScanIndexForward = true,

                ExclusiveStartKey = lastKey

            }).GetAwaiter().GetResult();

            foreach (var item in response.Items)

            {

                if (item.TryGetValue("finding", out var findingAttr) && findingAttr.M != null)

                    findings.Add(DynamoDbAttributeMap.ToFileFinding(findingAttr.M));

            }

            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;

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

            KeyConditionExpression = "jobId = :jobId",

            ExpressionAttributeValues = new Dictionary<string, AttributeValue>

            {

                [":jobId"] = new AttributeValue { S = jobId }

            },

            Select = Select.COUNT

        }).GetAwaiter().GetResult();

        return response.Count ?? 0;

    }

    // -------------------------------------------------------------------------

    // DeleteByJobId — cleanup after successful completion

    // -------------------------------------------------------------------------

    public void DeleteByJobId(string jobId)

    {

        if (string.IsNullOrWhiteSpace(jobId)) return;

        // Fetch all keys for this jobId (projected — don't load Finding data)

        var keysToDelete = new List<Dictionary<string, AttributeValue>>();

        Dictionary<string, AttributeValue>? lastKey = null;

        do

        {

            var response = _dynamoDb.QueryAsync(new QueryRequest

            {

                TableName = _tableName,

                KeyConditionExpression = "jobId = :jobId",

                ExpressionAttributeValues = new Dictionary<string, AttributeValue>

                {

                    [":jobId"] = new AttributeValue { S = jobId }

                },

                ProjectionExpression = "jobId, sequenceNumber",

                ExclusiveStartKey = lastKey

            }).GetAwaiter().GetResult();

            keysToDelete.AddRange(response.Items);

            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;

        }

        while (lastKey != null);

        if (keysToDelete.Count == 0) return;

        // Delete in batches of 25

        foreach (var chunk in keysToDelete.Chunk(BatchWriteLimit))

        {

            var deleteRequests = chunk

                .Select(key => new WriteRequest { DeleteRequest = new DeleteRequest { Key = key } })

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
