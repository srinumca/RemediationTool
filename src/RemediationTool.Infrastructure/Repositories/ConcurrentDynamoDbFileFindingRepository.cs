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
/// Preserves the existing repository behavior while accelerating large
/// ingestion writes with bounded DynamoDB concurrency.
/// </summary>
public sealed class ConcurrentDynamoDbFileFindingRepository : IFileFindingRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;

    private readonly DynamoDbFileFindingRepository _inner;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
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
        _maxConcurrentBatchWrites = Math.Clamp(
            processingOptions.Value.DynamoDbMaxConcurrentBatchWrites,
            1,
            16);
        _logger = logger;
    }

    public void Add(FileFinding finding)
        => _inner.Add(finding);

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();
        var totalBatchCount = CalculateBatchCount(findings.Count);

        try
        {
            BoundedBatchExecutor.Execute(
                findings.Count,
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
                        cancellationToken);
                });

            _logger.LogInformation(
                "[DYNAMODB_FINDINGS_WRITE_COMPLETE] Table:{Table}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                _tableName,
                findings.Count,
                totalBatchCount,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
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

    public void Update(FileFinding finding)
        => _inner.Update(finding);

    public FileFinding? GetById(Guid id)
        => _inner.GetById(id);

    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
        => _inner.GetLatestBySourceRecordId(sourceRecordId);

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
        => _inner.GetByIngestionJobId(ingestionJobId);

    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
        => _inner.GetLatestByFindingType(findingType);

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
        => _inner.GetLatestByDataSystem(dataSystem);

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
        => _inner.GetHistoryBySourceRecordId(sourceRecordId);

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        string? findingType = null)
        => _inner.GetLatestPaged(pageSize, lastEvaluatedKey, findingType);

    public IReadOnlyDictionary<string, int> GetCountByFindingType()
        => _inner.GetCountByFindingType();

    public int CountByFindingType(string findingType)
        => _inner.CountByFindingType(findingType);

    public List<FileFinding> GetAll()
        => _inner.GetAll();

    private static int CalculateBatchCount(int recordCount)
        => (recordCount + DynamoDbBatchLimit - 1) / DynamoDbBatchLimit;
}
