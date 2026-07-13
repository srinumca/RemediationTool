using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class BoundedBatchExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesEveryItemExactlyOnce()
    {
        const int totalItemCount = 103;
        var processedCounts = new int[totalItemCount];

        await BoundedBatchExecutor.ExecuteAsync(
            totalItemCount,
            batchSize: 25,
            maxDegreeOfParallelism: 4,
            async (range, cancellationToken) =>
            {
                var endExclusive = range.StartIndex + range.Count;
                for (var index = range.StartIndex; index < endExclusive; index++)
                    Interlocked.Increment(ref processedCounts[index]);

                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            });

        Assert.All(processedCounts, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotExceedConfiguredConcurrency()
    {
        const int configuredConcurrency = 3;
        var activeOperations = 0;
        var observedMaximum = 0;
        var completedItems = 0;

        await BoundedBatchExecutor.ExecuteAsync(
            totalItemCount: 100,
            batchSize: 10,
            maxDegreeOfParallelism: configuredConcurrency,
            async (range, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref activeOperations);
                UpdateMaximum(ref observedMaximum, active);

                try
                {
                    await Task.Delay(20, cancellationToken);
                    Interlocked.Add(ref completedItems, range.Count);
                }
                finally
                {
                    Interlocked.Decrement(ref activeOperations);
                }
            });

        Assert.Equal(100, completedItems);
        Assert.InRange(observedMaximum, 1, configuredConcurrency);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesBatchFailure()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedBatchExecutor.ExecuteAsync(
                totalItemCount: 75,
                batchSize: 25,
                maxDegreeOfParallelism: 2,
                (range, _) => range.BatchNumber == 2
                    ? Task.FromException(new InvalidOperationException("Simulated batch failure."))
                    : Task.CompletedTask));

        Assert.Equal("Simulated batch failure.", exception.Message);
    }

    [Theory]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(70_000, 25, 2_800)]
    public async Task ExecuteAsync_CreatesExpectedBatchCount(
        int totalItemCount,
        int batchSize,
        int expectedBatchCount)
    {
        var executedBatches = 0;

        await BoundedBatchExecutor.ExecuteAsync(
            totalItemCount,
            batchSize,
            maxDegreeOfParallelism: 4,
            (range, _) =>
            {
                Assert.InRange(range.Count, 1, batchSize);
                Interlocked.Increment(ref executedBatches);
                return Task.CompletedTask;
            });

        Assert.Equal(expectedBatchCount, executedBatches);
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current)
                return;

            if (Interlocked.CompareExchange(ref target, candidate, current) == current)
                return;
        }
    }
}
