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
/// Persists ingestion findings with bounded DynamoDB concurrency and provides
/// the dashboard lookup by ingestion job.
/// </summary>
public sealed class ConcurrentDynamoDbFileFindingRepository :
    IFileFindingRepository,
    IAsyncFileFindingRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;

    private readonly DynamoDbFileFindingRepository _inner;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly bool _enableBoundedConcurrency;
    private readonly int _maxConcurrentBatchWrites;
    private readonly ILogger<ConcurrentDynamoDbFileFindingRepository> _logger;

    public ConcurrentDynamoDbFileFindingRepository(
        DynamoDbFileFindingRepository inner,
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> dynamoDbOptions,
        IOptions<IngestionProcessingOptions> processingOptions,
        ILogger<ConcurrentDynamoDbFileFindingRepository> logger)
    {
        _inner = inner;
        _dynamoDb = dynamoDb;
        _tableName = dynamoDbOptions.Value.FindingsTableName;
        _enableBoundedConcurrency = processingOptions.Value.EnableBoundedDynamoDbConcurrency;
        _maxConcurrentBatchWrites = processingOptions.Value.ResolveDynamoDbWriteConcurrency();
        _logger = logger;
    }

    public void AddRange(IReadOnlyList<FileFinding> findings)
        => AddRangeAsync(findings).GetAwaiter().GetResult();

    public async Task AddRangeAsync(
        IReadOnlyList<FileFinding> findings,
        CancellationToken cancellationToken = default)
    {
        if (findings == null || findings.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableBoundedConcurrency)
        {
            _inner.AddRange(findings);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var totalBatchCount = CalculateBatchCount(findings.Count);

        try
        {
            await BoundedBatchExecutor.ExecuteAsync(
                findings.Count,
                DynamoDbBatchLimit,
                _maxConcurrentBatchWrites,
                async (range, token) =>
                {
                    var requests = new List<WriteRequest>(range.Count);
                    var endExclusive = range.StartIndex + range.Count;

                    for (var index = range.StartIndex; index < endExclusive; index++)
                    {
                        requests.Add(new WriteRequest
                        {
                            PutRequest = new PutRequest
                            {
                                Item = DynamoDbAttributeMap.ToMap(findings[index])
                            }
                        });
                    }

                    await DynamoDbBatchWriteExecutor.WriteAsync(
                        _dynamoDb,
                        _tableName,
                        requests,
                        operationName: "FindingsBatchWrite",
                        range.BatchNumber,
                        findings.Count,
                        MaxUnprocessedItemRetryAttempts,
                        _logger,
                        token);
                },
                cancellationToken);

            _logger.LogInformation(
                "[DYNAMODB_FINDINGS_WRITE_COMPLETE] Table:{Table}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                findings.Count,
                totalBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[DYNAMODB_FINDINGS_WRITE_CANCELLED] Table:{Table}, Records:{Records}, ElapsedMs:{ElapsedMs}",
                _tableName,
                findings.Count,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[DYNAMODB_FINDINGS_WRITE_FAILED] Table:{Table}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                findings.Count,
                totalBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
        => _inner.GetByIngestionJobId(ingestionJobId);

    private static int CalculateBatchCount(int recordCount)
        => (recordCount + DynamoDbBatchLimit - 1) / DynamoDbBatchLimit;
}
