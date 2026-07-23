using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;
using System.Diagnostics;
using System.Globalization;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Preserves staging as a complete resume fallback while accelerating its
/// write, read and cleanup paths with fully awaited bounded DynamoDB I/O.
/// </summary>
public sealed class ConcurrentDynamoDbIngestionStagingRepository :
    IIngestionStagingRepository,
    IAsyncIngestionStagingRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;
    private static readonly TimeSpan TtlDuration = TimeSpan.FromDays(7);

    private readonly DynamoDbIngestionStagingRepository _inner;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly bool _enableBoundedConcurrency;
    private readonly int _maxConcurrentBatchWrites;
    private readonly ILogger<ConcurrentDynamoDbIngestionStagingRepository> _logger;

    public ConcurrentDynamoDbIngestionStagingRepository(
        DynamoDbIngestionStagingRepository inner,
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> dynamoDbOptions,
        IOptions<IngestionProcessingOptions> processingOptions,
        ILogger<ConcurrentDynamoDbIngestionStagingRepository> logger)
    {
        _inner = inner;
        _dynamoDb = dynamoDb;
        _tableName = dynamoDbOptions.Value.StagedFindingsTableName;
        _enableBoundedConcurrency = processingOptions.Value.EnableBoundedDynamoDbConcurrency;
        _maxConcurrentBatchWrites = processingOptions.Value.ResolveDynamoDbWriteConcurrency();
        _logger = logger;
    }

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
        => SaveValidFindingsAsync(jobId, validFindings).GetAwaiter().GetResult();

    public async Task SaveValidFindingsAsync(
        string jobId,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableBoundedConcurrency)
        {
            _inner.SaveValidFindings(
                jobId,
                validFindings as List<FileFinding>
                ?? validFindings.ToList());
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await DeleteByJobIdAsync(jobId, cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var createdAtText = nowUtc.ToString("O", CultureInfo.InvariantCulture);
            var expiresAtText = new DateTimeOffset(nowUtc.Add(TtlDuration))
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);

            await BoundedBatchExecutor.ExecuteAsync(
                validFindings.Count,
                DynamoDbBatchLimit,
                _maxConcurrentBatchWrites,
                async (range, token) =>
                {
                    var requests = new List<WriteRequest>(range.Count);
                    var endExclusive = range.StartIndex + range.Count;

                    for (var index = range.StartIndex; index < endExclusive; index++)
                    {
                        var sequenceNumber = index + 1;
                        var item = new Dictionary<string, AttributeValue>(5)
                        {
                            ["jobId"] = new AttributeValue { S = jobId },
                            ["sequenceNumber"] = new AttributeValue
                            {
                                N = sequenceNumber.ToString(CultureInfo.InvariantCulture)
                            },
                            ["CreatedAtUtc"] = new AttributeValue { S = createdAtText },
                            ["ExpiresAt"] = new AttributeValue { N = expiresAtText },
                            ["finding"] = new AttributeValue
                            {
                                M = DynamoDbAttributeMap.ToMap(validFindings[index])
                            }
                        };

                        requests.Add(new WriteRequest
                        {
                            PutRequest = new PutRequest { Item = item }
                        });
                    }

                    await DynamoDbBatchWriteExecutor.WriteAsync(
                        _dynamoDb,
                        _tableName,
                        requests,
                        operationName: "StagingSave",
                        range.BatchNumber,
                        validFindings.Count,
                        MaxUnprocessedItemRetryAttempts,
                        _logger,
                        token);
                },
                cancellationToken);

            _logger.LogInformation(
                "[STAGING_SAVE_COMPLETE] JobId:{JobId}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                jobId,
                validFindings.Count,
                CalculateBatchCount(validFindings.Count),
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[STAGING_SAVE_CANCELLED] JobId:{JobId}, Records:{Records}, ElapsedMs:{ElapsedMs}",
                jobId,
                validFindings.Count,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[STAGING_SAVE_FAILED] JobId:{JobId}, Records:{Records}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                jobId,
                validFindings.Count,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public List<FileFinding> GetValidFindingsAfter(
        string jobId,
        int lastProcessedRecordCount)
        => GetValidFindingsAfterAsync(jobId, lastProcessedRecordCount)
            .GetAwaiter()
            .GetResult();

    public async Task<List<FileFinding>> GetValidFindingsAfterAsync(
        string jobId,
        int lastProcessedRecordCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new List<FileFinding>();

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableBoundedConcurrency)
            return _inner.GetValidFindingsAfter(jobId, lastProcessedRecordCount);

        _logger.LogInformation(
            "[STAGING_RESUME_READ] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            jobId,
            lastProcessedRecordCount);

        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _dynamoDb.QueryAsync(
                new QueryRequest
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
                },
                cancellationToken);

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
        => CountByJobIdAsync(jobId).GetAwaiter().GetResult();

    public async Task<int> CountByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return 0;

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableBoundedConcurrency)
            return _inner.CountByJobId(jobId);

        var count = 0;
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await _dynamoDb.QueryAsync(
                new QueryRequest
                {
                    TableName = _tableName,
                    KeyConditionExpression = "jobId = :jobId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":jobId"] = new AttributeValue { S = jobId }
                    },
                    Select = Select.COUNT,
                    ExclusiveStartKey = lastKey
                },
                cancellationToken);

            count += response.Count ?? 0;
            lastKey = GetNextKey(response.LastEvaluatedKey);
        }
        while (lastKey != null);

        return count;
    }

    public void DeleteByJobId(string jobId)
        => DeleteByJobIdAsync(jobId).GetAwaiter().GetResult();

    public async Task DeleteByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enableBoundedConcurrency)
        {
            _inner.DeleteByJobId(jobId);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Dictionary<string, AttributeValue>? lastKey = null;
        var deletedCount = 0;
        var pageNumber = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageNumber++;

            var response = await _dynamoDb.QueryAsync(
                new QueryRequest
                {
                    TableName = _tableName,
                    KeyConditionExpression = "jobId = :jobId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":jobId"] = new AttributeValue { S = jobId }
                    },
                    ProjectionExpression = "jobId, sequenceNumber",
                    ExclusiveStartKey = lastKey
                },
                cancellationToken);

            lastKey = GetNextKey(response.LastEvaluatedKey);

            if (response.Items.Count == 0)
                continue;

            await BoundedBatchExecutor.ExecuteAsync(
                response.Items.Count,
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
                            DeleteRequest = new DeleteRequest
                            {
                                Key = response.Items[index]
                            }
                        });
                    }

                    await DynamoDbBatchWriteExecutor.WriteAsync(
                        _dynamoDb,
                        _tableName,
                        requests,
                        operationName: "StagingDelete",
                        range.BatchNumber,
                        response.Items.Count,
                        MaxUnprocessedItemRetryAttempts,
                        _logger,
                        token);
                },
                cancellationToken);

            deletedCount += response.Items.Count;
        }
        while (lastKey != null);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "[STAGING_DELETE_COMPLETE] JobId:{JobId}, Records:{Records}, Pages:{Pages}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                jobId,
                deletedCount,
                pageNumber,
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static int CalculateBatchCount(int recordCount)
        => (recordCount + DynamoDbBatchLimit - 1) / DynamoDbBatchLimit;

    private static Dictionary<string, AttributeValue>? GetNextKey(
        Dictionary<string, AttributeValue>? lastEvaluatedKey)
        => lastEvaluatedKey?.Count > 0 ? lastEvaluatedKey : null;
}
