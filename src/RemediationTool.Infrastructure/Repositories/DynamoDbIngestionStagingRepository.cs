using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;
using System.Globalization;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Persists valid ingestion records temporarily so resume can fall back to
/// DynamoDB when a Parquet working file is unavailable or unreadable.
/// </summary>
public class DynamoDbIngestionStagingRepository : IIngestionStagingRepository
{
    private const int BatchWriteLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;
    private const int MaximumSupportedBatchWriteConcurrency = 16;
    private static readonly TimeSpan TtlDuration = TimeSpan.FromDays(7);

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly int _maxBatchWriteConcurrency;
    private readonly ILogger<DynamoDbIngestionStagingRepository> _logger;

    public DynamoDbIngestionStagingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbIngestionStagingRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.StagedFindingsTableName;
        _maxBatchWriteConcurrency = Math.Clamp(
            options.Value.MaxBatchWriteConcurrency,
            1,
            MaximumSupportedBatchWriteConcurrency);
        _logger = logger;
    }

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0)
            return;

        _logger.LogInformation(
            "[STAGING_SAVE_START] JobId:{JobId}, Records:{Records}, BatchWriteLimit:{BatchWriteLimit}, MaxConcurrency:{MaxConcurrency}",
            jobId,
            validFindings.Count,
            BatchWriteLimit,
            _maxBatchWriteConcurrency);

        DeleteByJobId(jobId);

        var nowUtc = DateTime.UtcNow;
        var createdAtText = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        var expiresAtText = new DateTimeOffset(nowUtc.Add(TtlDuration))
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var batchCount = (validFindings.Count + BatchWriteLimit - 1) / BatchWriteLimit;

        try
        {
            Parallel.For(
                0,
                batchCount,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxBatchWriteConcurrency
                },
                batchIndex =>
                {
                    var startIndex = batchIndex * BatchWriteLimit;
                    var count = Math.Min(BatchWriteLimit, validFindings.Count - startIndex);
                    var writeRequests = new List<WriteRequest>(count);

                    for (var offset = 0; offset < count; offset++)
                    {
                        var findingIndex = startIndex + offset;
                        var item = new Dictionary<string, AttributeValue>(5)
                        {
                            ["jobId"] = new AttributeValue { S = jobId },
                            ["sequenceNumber"] = new AttributeValue
                            {
                                N = (findingIndex + 1).ToString(CultureInfo.InvariantCulture)
                            },
                            ["CreatedAtUtc"] = new AttributeValue { S = createdAtText },
                            ["ExpiresAt"] = new AttributeValue { N = expiresAtText },
                            ["finding"] = new AttributeValue
                            {
                                M = DynamoDbAttributeMap.ToMap(validFindings[findingIndex])
                            }
                        };

                        writeRequests.Add(new WriteRequest
                        {
                            PutRequest = new PutRequest { Item = item }
                        });
                    }

                    ExecuteBatchWriteWithRetry(
                        writeRequests,
                        operationName: "StagingSave",
                        jobId,
                        chunkNumber: batchIndex + 1,
                        totalInputCount: validFindings.Count);
                });
        }
        catch (AggregateException ex)
        {
            throw new InvalidOperationException(
                $"Staging save failed for job {jobId}. A subsequent retry first removes partial staging rows and recreates the complete ordered recovery set.",
                ex.Flatten());
        }

        _logger.LogInformation(
            "[STAGING_SAVE_COMPLETE] JobId:{JobId}, Records:{Records}, Chunks:{Chunks}",
            jobId,
            validFindings.Count,
            batchCount);
    }

    public List<FileFinding> GetValidFindingsAfter(
        string jobId,
        int lastProcessedRecordCount)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new List<FileFinding>();

        _logger.LogInformation(
            "[STAGING_RESUME_READ] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            jobId,
            lastProcessedRecordCount);

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
                    [":lastSeq"] = new AttributeValue
                    {
                        N = lastProcessedRecordCount.ToString(CultureInfo.InvariantCulture)
                    }
                },
                ScanIndexForward = true,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            findings.EnsureCapacity(findings.Count + response.Items.Count);
            foreach (var item in response.Items)
            {
                if (item.TryGetValue("finding", out var findingAttribute)
                    && findingAttribute.M != null)
                {
                    findings.Add(DynamoDbAttributeMap.ToFileFinding(findingAttribute.M));
                }
            }

            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        _logger.LogInformation(
            "[STAGING_RESUME_READ_COMPLETE] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}, Records:{Records}",
            jobId,
            lastProcessedRecordCount,
            findings.Count);

        return findings;
    }

    public int CountByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return 0;

        var count = 0;
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
                Select = Select.COUNT,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            count += response.Count ?? 0;
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return count;
    }

    public void DeleteByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        Dictionary<string, AttributeValue>? lastKey = null;
        var deletedCount = 0;
        var chunkNumber = 0;

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

            lastKey = GetNextKey(response.LastEvaluatedKey);
            var pageBatchCount = (response.Items.Count + BatchWriteLimit - 1) / BatchWriteLimit;
            var pageChunkStart = chunkNumber;

            try
            {
                Parallel.For(
                    0,
                    pageBatchCount,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _maxBatchWriteConcurrency
                    },
                    batchIndex =>
                    {
                        var startIndex = batchIndex * BatchWriteLimit;
                        var count = Math.Min(BatchWriteLimit, response.Items.Count - startIndex);
                        var deleteRequests = new List<WriteRequest>(count);

                        for (var offset = 0; offset < count; offset++)
                        {
                            deleteRequests.Add(new WriteRequest
                            {
                                DeleteRequest = new DeleteRequest
                                {
                                    Key = response.Items[startIndex + offset]
                                }
                            });
                        }

                        ExecuteBatchWriteWithRetry(
                            deleteRequests,
                            operationName: "StagingDelete",
                            jobId,
                            chunkNumber: pageChunkStart + batchIndex + 1,
                            totalInputCount: response.Items.Count);
                    });
            }
            catch (AggregateException ex)
            {
                throw new InvalidOperationException(
                    $"Staging cleanup failed for job {jobId}. Remaining rows retain their TTL and can be cleaned safely on the next attempt.",
                    ex.Flatten());
            }

            chunkNumber += pageBatchCount;
            deletedCount += response.Items.Count;
        }
        while (lastKey != null);

        if (deletedCount == 0)
            return;

        _logger.LogInformation(
            "[STAGING_DELETE_COMPLETE] JobId:{JobId}, Records:{Records}, Chunks:{Chunks}",
            jobId,
            deletedCount,
            chunkNumber);
    }

    private void ExecuteBatchWriteWithRetry(
        List<WriteRequest> writeRequests,
        string operationName,
        string jobId,
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
                    "[STAGING_BATCH_WRITE_COMPLETE] Operation:{Operation}, JobId:{JobId}, Table:{Table}, ChunkNumber:{ChunkNumber}, ChunkSize:{ChunkSize}, TotalInputCount:{TotalInputCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    jobId,
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
                    "[STAGING_BATCH_WRITE_UNPROCESSED_EXHAUSTED] Operation:{Operation}, JobId:{JobId}, Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    jobId,
                    _tableName,
                    chunkNumber,
                    unprocessedCount,
                    retryAttempt);

                throw new InvalidOperationException(
                    $"{operationName} failed for job {jobId} in table {_tableName}. {unprocessedCount} item(s) remained unprocessed after {retryAttempt} retry attempt(s).");
            }

            var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt));
            _logger.LogWarning(
                "[STAGING_BATCH_WRITE_UNPROCESSED_RETRY] Operation:{Operation}, JobId:{JobId}, Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempt:{RetryAttempt}, DelayMs:{DelayMs}",
                operationName,
                jobId,
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

    private static Dictionary<string, AttributeValue>? GetNextKey(
        Dictionary<string, AttributeValue>? lastEvaluatedKey)
        => lastEvaluatedKey?.Count > 0 ? lastEvaluatedKey : null;
}
