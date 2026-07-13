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
/// write and cleanup paths with bounded DynamoDB concurrency.
/// </summary>
public sealed class ConcurrentDynamoDbIngestionStagingRepository : IIngestionStagingRepository
{
    private const int DynamoDbBatchLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;
    private static readonly TimeSpan TtlDuration = TimeSpan.FromDays(7);

    private readonly DynamoDbIngestionStagingRepository _inner;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
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
        _maxConcurrentBatchWrites = Math.Clamp(
            processingOptions.Value.DynamoDbMaxConcurrentBatchWrites,
            1,
            16);
        _logger = logger;
    }

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            DeleteByJobId(jobId);

            var nowUtc = DateTime.UtcNow;
            var createdAtText = nowUtc.ToString("O", CultureInfo.InvariantCulture);
            var expiresAtText = new DateTimeOffset(nowUtc.Add(TtlDuration))
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);

            BoundedBatchExecutor.Execute(
                validFindings.Count,
                DynamoDbBatchLimit,
                _maxConcurrentBatchWrites,
                async (range, cancellationToken) =>
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
                        cancellationToken);
                });

            _logger.LogInformation(
                "[STAGING_SAVE_COMPLETE] JobId:{JobId}, Records:{Records}, Batches:{Batches}, MaxConcurrency:{MaxConcurrency}, ElapsedMs:{ElapsedMs}",
                jobId,
                validFindings.Count,
                CalculateBatchCount(validFindings.Count),
                _maxConcurrentBatchWrites,
                stopwatch.ElapsedMilliseconds);
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
        => _inner.GetValidFindingsAfter(jobId, lastProcessedRecordCount);

    public int CountByJobId(string jobId)
        => _inner.CountByJobId(jobId);

    public void DeleteByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        DeleteByJobIdAsync(jobId).GetAwaiter().GetResult();
    }

    private async Task DeleteByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
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

            lastKey = response.LastEvaluatedKey?.Count > 0
                ? response.LastEvaluatedKey
                : null;

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
}
