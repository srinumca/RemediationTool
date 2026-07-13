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
public sealed class ConcurrentDynamoDbRejectedRowRepository : IRejectedRowRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;

    private readonly DynamoDbRejectedRowRepository _inner;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly int _maxConcurrentBatchWrites;
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
        _maxConcurrentBatchWrites = Math.Clamp(
            processingOptions.Value.DynamoDbMaxConcurrentBatchWrites,
            1,
            16);
        _logger = logger;
    }

    public List<RejectedRowDetail> GetAll()
        => _inner.GetAll();

    public List<RejectedRowDetail> GetByJobId(string jobId)
        => _inner.GetByJobId(jobId);

    public void AddRange(List<RejectedRowDetail> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();
        var totalBatchCount = CalculateBatchCount(rejectedRows.Count);

        try
        {
            BoundedBatchExecutor.Execute(
                rejectedRows.Count,
                DynamoDbBatchLimit,
                _maxConcurrentBatchWrites,
                async (range, cancellationToken) =>
                {
                    var requests = new List<WriteRequest>(range.Count);
                    var endExclusive = range.StartIndex + range.Count;

                    for (var index = range.StartIndex; index < endExclusive; index++)
                    {
                        requests.Add(new WriteRequest
                        {
                            PutRequest = new PutRequest
                            {
                                Item = DynamoDbAttributeMap.ToMap(rejectedRows[index])
                            }
                        });
                    }

                    await DynamoDbBatchWriteExecutor.WriteAsync(
                        _dynamoDb,
                        _tableName,
                        requests,
                        operationName: "RejectedRowsBatchWrite",
                        range.BatchNumber,
                        rejectedRows.Count,
                        MaxUnprocessedItemRetryAttempts,
                        _logger,
                        cancellationToken);
                });

            _logger.LogInformation(
                "[DYNAMODB_REJECTED_ROWS_WRITE_COMPLETE] Table:{Table}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                rejectedRows.Count,
                totalBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[DYNAMODB_REJECTED_ROWS_WRITE_FAILED] Table:{Table}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                rejectedRows.Count,
                totalBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static int CalculateBatchCount(int recordCount)
        => (recordCount + DynamoDbBatchLimit - 1) / DynamoDbBatchLimit;
}
