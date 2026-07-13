using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Executes one DynamoDB BatchWriteItem request and retries only the items
/// returned by DynamoDB as unprocessed. The method returns only after every
/// item in the batch has been accepted.
/// </summary>
internal static class DynamoDbBatchWriteExecutor
{
    public static async Task WriteAsync(
        IAmazonDynamoDB dynamoDb,
        string tableName,
        IReadOnlyList<WriteRequest> writeRequests,
        string operationName,
        int batchNumber,
        int totalInputCount,
        int maxUnprocessedItemRetryAttempts,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dynamoDb);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(writeRequests);
        ArgumentNullException.ThrowIfNull(logger);

        if (writeRequests.Count == 0)
            return;

        var remaining = new Dictionary<string, List<WriteRequest>>
        {
            [tableName] = writeRequests is List<WriteRequest> list
                ? list
                : writeRequests.ToList()
        };

        var retryAttempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await dynamoDb.BatchWriteItemAsync(
                new BatchWriteItemRequest { RequestItems = remaining },
                cancellationToken);

            var unprocessedCount = CountUnprocessed(response.UnprocessedItems);
            if (unprocessedCount == 0)
            {
                logger.LogDebug(
                    "[DYNAMODB_BATCH_WRITE_COMPLETE] Operation:{Operation}, Table:{Table}, BatchNumber:{BatchNumber}, BatchSize:{BatchSize}, TotalInputCount:{TotalInputCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    tableName,
                    batchNumber,
                    writeRequests.Count,
                    totalInputCount,
                    retryAttempt);
                return;
            }

            retryAttempt++;
            if (retryAttempt >= Math.Max(1, maxUnprocessedItemRetryAttempts))
            {
                logger.LogError(
                    "[DYNAMODB_BATCH_WRITE_EXHAUSTED] Operation:{Operation}, Table:{Table}, BatchNumber:{BatchNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    tableName,
                    batchNumber,
                    unprocessedCount,
                    retryAttempt);

                throw new InvalidOperationException(
                    $"{operationName} failed for table {tableName}. {unprocessedCount} item(s) remained unprocessed after {retryAttempt} retry attempt(s).");
            }

            var exponentialDelayMilliseconds = 100 * Math.Pow(2, retryAttempt);
            var jitterMilliseconds = Random.Shared.Next(0, 101);
            var delay = TimeSpan.FromMilliseconds(
                exponentialDelayMilliseconds + jitterMilliseconds);

            logger.LogWarning(
                "[DYNAMODB_BATCH_WRITE_RETRY] Operation:{Operation}, Table:{Table}, BatchNumber:{BatchNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempt:{RetryAttempt}, DelayMs:{DelayMs}",
                operationName,
                tableName,
                batchNumber,
                unprocessedCount,
                retryAttempt,
                delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken);
            remaining = response.UnprocessedItems;
        }
    }

    private static int CountUnprocessed(
        Dictionary<string, List<WriteRequest>>? unprocessedItems)
    {
        if (unprocessedItems == null || unprocessedItems.Count == 0)
            return 0;

        var count = 0;
        foreach (var items in unprocessedItems.Values)
            count += items.Count;

        return count;
    }
}
