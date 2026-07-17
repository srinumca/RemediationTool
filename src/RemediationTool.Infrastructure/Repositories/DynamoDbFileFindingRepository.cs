using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB finding persistence required by ingestion.
/// </summary>
public class DynamoDbFileFindingRepository : IFileFindingRepository
{
    private const int BatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;
    private const int MaximumSupportedBatchWriteConcurrency = 16;

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly int _maxBatchWriteConcurrency;
    private readonly ILogger<DynamoDbFileFindingRepository> _logger;

    public DynamoDbFileFindingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbFileFindingRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.FindingsTableName;
        _maxBatchWriteConcurrency = Math.Clamp(
            options.Value.MaxBatchWriteConcurrency,
            1,
            MaximumSupportedBatchWriteConcurrency);
        _logger = logger;
    }

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0)
            return;

        var batches = findings
            .Chunk(BatchLimit)
            .Select((chunk, index) => new BatchWriteWorkItem(
                index + 1,
                BuildPutRequests(chunk)))
            .ToArray();

        _logger.LogInformation(
            "[DYNAMODB_BATCH_WRITE_START] Operation:{Operation}, Table:{Table}, TotalInputCount:{TotalInputCount}, BatchCount:{BatchCount}, MaxConcurrency:{MaxConcurrency}",
            "FindingsBatchWrite",
            _tableName,
            findings.Count,
            batches.Length,
            _maxBatchWriteConcurrency);

        try
        {
            Parallel.ForEach(
                batches,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxBatchWriteConcurrency
                },
                batch => ExecuteBatchWriteWithRetry(
                    batch.Requests,
                    operationName: "FindingsBatchWrite",
                    batch.ChunkNumber,
                    totalInputCount: findings.Count));
        }
        catch (AggregateException ex)
        {
            throw new InvalidOperationException(
                $"Findings batch persistence failed for table {_tableName}. All successful writes remain idempotent and the application checkpoint will retry the complete outer batch.",
                ex.Flatten());
        }

        _logger.LogInformation(
            "[DYNAMODB_BATCH_WRITE_ALL_COMPLETE] Operation:{Operation}, Table:{Table}, TotalInputCount:{TotalInputCount}, BatchCount:{BatchCount}",
            "FindingsBatchWrite",
            _tableName,
            findings.Count,
            batches.Length);
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

    private static List<WriteRequest> BuildPutRequests(FileFinding[] findings)
    {
        var requests = new List<WriteRequest>(findings.Length);
        foreach (var finding in findings)
        {
            requests.Add(new WriteRequest
            {
                PutRequest = new PutRequest
                {
                    Item = DynamoDbAttributeMap.ToMap(finding)
                }
            });
        }

        return requests;
    }

    private sealed record BatchWriteWorkItem(
        int ChunkNumber,
        List<WriteRequest> Requests);
}
