namespace RemediationTool.Infrastructure.Repositories;

internal readonly record struct BatchRange(
    int BatchNumber,
    int StartIndex,
    int Count);

/// <summary>
/// Executes indexed batches with bounded concurrency and verifies that every
/// input item belongs to one successfully completed batch.
/// </summary>
internal static class BoundedBatchExecutor
{
    public static void Execute(
        int totalItemCount,
        int batchSize,
        int maxDegreeOfParallelism,
        Func<BatchRange, CancellationToken, Task> processBatch,
        CancellationToken cancellationToken = default)
    {
        ExecuteAsync(
                totalItemCount,
                batchSize,
                maxDegreeOfParallelism,
                processBatch,
                cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task ExecuteAsync(
        int totalItemCount,
        int batchSize,
        int maxDegreeOfParallelism,
        Func<BatchRange, CancellationToken, Task> processBatch,
        CancellationToken cancellationToken = default)
    {
        if (totalItemCount < 0)
            throw new ArgumentOutOfRangeException(nameof(totalItemCount));

        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        ArgumentNullException.ThrowIfNull(processBatch);

        if (totalItemCount == 0)
            return;

        var batchCount = (totalItemCount + batchSize - 1) / batchSize;
        var boundedParallelism = Math.Clamp(maxDegreeOfParallelism, 1, batchCount);
        var completedItemCount = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, batchCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = boundedParallelism,
                CancellationToken = cancellationToken
            },
            async (zeroBasedBatchIndex, token) =>
            {
                var startIndex = zeroBasedBatchIndex * batchSize;
                var count = Math.Min(batchSize, totalItemCount - startIndex);
                var range = new BatchRange(
                    zeroBasedBatchIndex + 1,
                    startIndex,
                    count);

                await processBatch(range, token);
                Interlocked.Add(ref completedItemCount, count);
            });

        if (completedItemCount != totalItemCount)
        {
            throw new InvalidOperationException(
                $"Batch execution completed {completedItemCount} of {totalItemCount} item(s). No checkpoint should advance for this operation.");
        }
    }
}
