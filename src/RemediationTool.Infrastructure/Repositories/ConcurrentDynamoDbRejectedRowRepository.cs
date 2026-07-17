using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;
using System.Diagnostics;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Keeps rejected-row persistence lossless while using bounded concurrency for
/// large invalid-row sets.
/// </summary>
public sealed class ConcurrentDynamoDbRejectedRowRepository :
    IRejectedRowRepository,
    IAsyncRejectedRowRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;

    private readonly DynamoDbRejectedRowRepository _inner;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly bool _enableBoundedConcurrency;
    private readonly int _maxConcurrentBatchWrites;
    private readonly int _rejectedRowBatchSize;
    private readonly ILogger<ConcurrentDynamoDbRejectedRowRepository> _logger;

    public ConcurrentDynamoDbRejectedRowRepository(
        DynamoDbRejectedRowRepository inner,
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> dynamoDbOptions,
        IOptions<IngestionProcessingOptions> processingOptions,
        ILogger<ConcurrentDynamoDbRejectedRowRepository> logger)
    {
        _inner = inner;
        _dynamoDb = dynamoDb;
        _tableName = dynamoDbOptions.Value.RejectedRowsTableName;
        _enableBoundedConcurrency = processingOptions.Value.EnableBoundedDynamoDbConcurrency;
        _maxConcurrentBatchWrites = processingOptions.Value.ResolveDynamoDbWriteConcurrency();
        _rejectedRowBatchSize = processingOptions.Value.ResolveRejectedRowBatchSize();
        _logger = logger;
    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)
        => AddRangeAsync(rejectedRows).GetAwaiter().GetResult();

    public async Task AddRangeAsync(
        IReadOnlyList<RejectedRowDetail> rejectedRows,
        CancellationToken cancellationToken = default)
    {
        if (rejectedRows == null || rejectedRows.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableBoundedConcurrency)
        {
            _inner.AddRange(
                rejectedRows as List<RejectedRowDetail>
                ?? rejectedRows.ToList());
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var totalDynamoBatchCount = CalculateBatchCount(rejectedRows.Count, DynamoDbBatchLimit);
        var totalApplicationBatchCount = CalculateBatchCount(rejectedRows.Count, _rejectedRowBatchSize);

        try
        {
            for (var applicationStart = 0;
                 applicationStart < rejectedRows.Count;
                 applicationStart += _rejectedRowBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var applicationCount = Math.Min(
                    _rejectedRowBatchSize,
                    rejectedRows.Count - applicationStart);
                var applicationOffset = applicationStart;

                await BoundedBatchExecutor.ExecuteAsync(
                    applicationCount,
                    DynamoDbBatchLimit,
                    _maxConcurrentBatchWrites,
                    async (range, token) =>
                    {
                        var requests = new List<WriteRequest>(range.Count);
                        var globalStartIndex = applicationOffset + range.StartIndex;
                        var endExclusive = globalStartIndex + range.Count;

                        for (var index = globalStartIndex; index < endExclusive; index++)
                        {
                            requests.Add(new WriteRequest
                            {
                                PutRequest = new PutRequest
                                {
                                    Item = DynamoDbAttributeMap.ToMap(rejectedRows[index])
                                }
                            });
                        }

                        var globalBatchNumber = globalStartIndex / DynamoDbBatchLimit + 1;
                        await DynamoDbBatchWriteExecutor.WriteAsync(
                            _dynamoDb,
                            _tableName,
                            requests,
                            operationName: "RejectedRowsBatchWrite",
                            globalBatchNumber,
                            rejectedRows.Count,
                            MaxUnprocessedItemRetryAttempts,
                            _logger,
                            token);
                    },
                    cancellationToken);
            }

            _logger.LogInformation(
                "[DYNAMODB_REJECTED_ROWS_WRITE_COMPLETE] Table:{Table}, Records:{Records}, ApplicationBatches:{ApplicationBatches}, DynamoBatches:{DynamoBatches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                rejectedRows.Count,
                totalApplicationBatchCount,
                totalDynamoBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[DYNAMODB_REJECTED_ROWS_WRITE_CANCELLED] Table:{Table}, Records:{Records}, ElapsedMs:{ElapsedMs}",
                _tableName,
                rejectedRows.Count,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[DYNAMODB_REJECTED_ROWS_WRITE_FAILED] Table:{Table}, Records:{Records}, DynamoBatches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                rejectedRows.Count,
                totalDynamoBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static int CalculateBatchCount(int recordCount, int batchSize)
        => (recordCount + batchSize - 1) / batchSize;
}
