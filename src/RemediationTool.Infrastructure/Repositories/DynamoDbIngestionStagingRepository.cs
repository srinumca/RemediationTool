using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Strategies;

namespace RemediationTool.Infrastructure.Repositories;

public class DynamoDbIngestionStagingRepository : IIngestionStagingRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IIngestionWorkingFileStrategy _workingFileStrategy;
    private readonly IngestionProcessingOptions _processingOptions;
    private readonly ILogger<DynamoDbIngestionStagingRepository> _logger;

    private const int BatchWriteLimit = 25;
    private const int MaxUnprocessedItemRetryAttempts = 5;
    private static readonly TimeSpan TtlDuration = TimeSpan.FromDays(7);

    public DynamoDbIngestionStagingRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        IStorageService storage,
        IIngestionJobAuditRepository jobAuditRepository,
        IOptions<IngestionProcessingOptions> processingOptions,
        ILogger<DynamoDbIngestionStagingRepository> logger,
        ILogger<ParquetIngestionWorkingFileStrategy> parquetLogger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.StagedFindingsTableName;
        _jobAuditRepository = jobAuditRepository;
        _processingOptions = processingOptions.Value;
        _logger = logger;
        _workingFileStrategy = new ParquetIngestionWorkingFileStrategy(storage, processingOptions, parquetLogger);
    }

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0) return;

        _logger.LogInformation(
            "[STAGING_SAVE_START] JobId:{JobId}, Records:{Records}, BatchWriteLimit:{BatchWriteLimit}",
            jobId,
            validFindings.Count,
            BatchWriteLimit);

        DeleteByJobId(jobId);

        var expiresAt = DateTimeOffset.UtcNow.Add(TtlDuration).ToUnixTimeSeconds();
        var sequenceNumber = 1;
        var chunkNumber = 0;

        foreach (var chunk in validFindings.Chunk(BatchWriteLimit))
        {
            chunkNumber++;
            var writeRequests = new List<WriteRequest>();

            foreach (var finding in chunk)
            {
                var item = new Dictionary<string, AttributeValue>
                {
                    ["jobId"] = new AttributeValue { S = jobId },
                    ["sequenceNumber"] = new AttributeValue { N = sequenceNumber.ToString() },
                    ["CreatedAtUtc"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") },
                    ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() },
                    ["finding"] = new AttributeValue { M = DynamoDbAttributeMap.ToMap(finding) }
                };

                writeRequests.Add(new WriteRequest { PutRequest = new PutRequest { Item = item } });
                sequenceNumber++;
            }

            ExecuteBatchWriteWithRetry(
                writeRequests,
                operationName: "StagingSave",
                jobId: jobId,
                chunkNumber: chunkNumber,
                totalInputCount: validFindings.Count);
        }

        WriteParquetWorkingFile(jobId, validFindings);

        _logger.LogInformation(
            "[STAGING_SAVE_COMPLETE] JobId:{JobId}, Records:{Records}, Chunks:{Chunks}",
            jobId,
            validFindings.Count,
            chunkNumber);
    }

    public List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return new List<FileFinding>();

        var parquetRecords = TryReadFromParquet(jobId, lastProcessedRecordCount);
        if (parquetRecords != null)
            return parquetRecords;

        _logger.LogInformation(
            "[STAGING_RESUME_READ] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            jobId, lastProcessedRecordCount);

        var findings = new List<FileFinding>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "jobId = :jobId AND sequenceNumber > :lastSeq",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = jobId },
                    [":lastSeq"] = new AttributeValue { N = lastProcessedRecordCount.ToString() }
                },
                ScanIndexForward = true,
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            foreach (var item in response.Items)
            {
                if (item.TryGetValue("finding", out var findingAttr) && findingAttr.M != null)
                    findings.Add(DynamoDbAttributeMap.ToFileFinding(findingAttr.M));
            }

            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
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
    {
        if (string.IsNullOrWhiteSpace(jobId)) return 0;

        var response = _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "jobId = :jobId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":jobId"] = new AttributeValue { S = jobId }
            },
            Select = Select.COUNT
        }).GetAwaiter().GetResult();

        var stagingCount = response.Count ?? 0;
        if (stagingCount > 0 || !_processingOptions.EnableParquetWorkingFile)
            return stagingCount;

        var audit = _jobAuditRepository.GetByJobId(jobId);
        return IsParquetAvailable(audit) ? audit!.WorkingFileRecordCount : 0;
    }

    public void DeleteByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return;

        var keysToDelete = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "jobId = :jobId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobId"] = new AttributeValue { S = jobId }
                },
                ProjectionExpression = "jobId, sequenceNumber",
                ExclusiveStartKey = lastKey
            }).GetAwaiter().GetResult();

            keysToDelete.AddRange(response.Items);
            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;
        }
        while (lastKey != null);

        if (keysToDelete.Count == 0) return;

        _logger.LogInformation(
            "[STAGING_DELETE_START] JobId:{JobId}, Records:{Records}",
            jobId,
            keysToDelete.Count);

        var chunkNumber = 0;
        foreach (var chunk in keysToDelete.Chunk(BatchWriteLimit))
        {
            chunkNumber++;
            var deleteRequests = chunk
                .Select(key => new WriteRequest { DeleteRequest = new DeleteRequest { Key = key } })
                .ToList();

            ExecuteBatchWriteWithRetry(
                deleteRequests,
                operationName: "StagingDelete",
                jobId: jobId,
                chunkNumber: chunkNumber,
                totalInputCount: keysToDelete.Count);
        }

        _logger.LogInformation(
            "[STAGING_DELETE_COMPLETE] JobId:{JobId}, Records:{Records}, Chunks:{Chunks}",
            jobId,
            keysToDelete.Count,
            chunkNumber);
    }

    private void WriteParquetWorkingFile(string jobId, List<FileFinding> validFindings)
    {
        if (!_processingOptions.EnableParquetWorkingFile) return;

        var audit = _jobAuditRepository.GetByJobId(jobId);
        if (audit == null)
        {
            _logger.LogWarning("[PARQUET_WRITE_SKIPPED] JobId:{JobId}, Reason:Job audit not found.", jobId);
            return;
        }

        _logger.LogInformation(
            "[PARQUET_STAGING_WRITE_START] JobId:{JobId}, InboundFileName:{InboundFileName}, Records:{Records}",
            jobId, audit.InboundFileName, validFindings.Count);

        var result = _workingFileStrategy
            .WriteAsync(jobId, audit.InboundFileName, validFindings)
            .GetAwaiter()
            .GetResult();

        audit.WorkingFileFormat = result.Format;
        audit.WorkingFilePath = result.Path;
        audit.WorkingFileRecordCount = result.RecordCount;
        _jobAuditRepository.Update(audit);

        _logger.LogInformation(
            "[PARQUET_STAGING_WRITE_COMPLETE] JobId:{JobId}, Path:{Path}, Records:{Records}",
            jobId, result.Path, result.RecordCount);
    }

    private List<FileFinding>? TryReadFromParquet(string jobId, int lastProcessedRecordCount)
    {
        if (!_processingOptions.EnableParquetWorkingFile) return null;

        var audit = _jobAuditRepository.GetByJobId(jobId);
        if (!IsParquetAvailable(audit)) return null;

        try
        {
            _logger.LogInformation(
                "[PARQUET_RESUME_READ_ATTEMPT] JobId:{JobId}, Path:{Path}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                jobId, audit!.WorkingFilePath, lastProcessedRecordCount);

            var records = _workingFileStrategy
                .ReadAfterAsync(audit.WorkingFilePath!, lastProcessedRecordCount)
                .GetAwaiter()
                .GetResult();

            if (records.Count > 0 || lastProcessedRecordCount >= audit.WorkingFileRecordCount)
            {
                _logger.LogInformation("[PARQUET_RESUME_READ_SUCCESS] JobId:{JobId}, Records:{Records}", jobId, records.Count);
                return records;
            }

            _logger.LogWarning("[PARQUET_RESUME_EMPTY_FALLBACK] JobId:{JobId}, Path:{Path}", jobId, audit.WorkingFilePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PARQUET_RESUME_READ_FAILED] JobId:{JobId}. Falling back to staging.", jobId);
            return null;
        }
    }

    private void ExecuteBatchWriteWithRetry(
        List<WriteRequest> writeRequests,
        string operationName,
        string jobId,
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

            if (response.UnprocessedItems == null || !response.UnprocessedItems.Any())
            {
                _logger.LogInformation(
                    "[STAGING_BATCH_WRITE_COMPLETE] Operation:{Operation}, JobId:{JobId}, Table:{Table}, ChunkNumber:{ChunkNumber}, ChunkSize:{ChunkSize}, TotalInputCount:{TotalInputCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    jobId,
                    _tableName,
                    chunkNumber,
                    writeRequests.Count,
                    totalInputCount,
                    retryAttempt);
                return;
            }

            var unprocessedCount = response.UnprocessedItems.Values.Sum(items => items.Count);
            retryAttempt++;

            if (retryAttempt >= MaxUnprocessedItemRetryAttempts)
            {
                _logger.LogError(
                    "[STAGING_BATCH_WRITE_UNPROCESSED_EXHAUSTED] Operation:{Operation}, JobId:{JobId}, Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempts:{RetryAttempts}",
                    operationName,
                    jobId,
                    _tableName,
                    chunkNumber,
                    unprocessedCount,
                    retryAttempt);

                throw new InvalidOperationException(
                    $"{operationName} failed for job {jobId} in table {_tableName}. {unprocessedCount} item(s) remained unprocessed after {retryAttempt} retry attempt(s).");
            }

            var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt));
            _logger.LogWarning(
                "[STAGING_BATCH_WRITE_UNPROCESSED_RETRY] Operation:{Operation}, JobId:{JobId}, Table:{Table}, ChunkNumber:{ChunkNumber}, UnprocessedCount:{UnprocessedCount}, RetryAttempt:{RetryAttempt}, DelayMs:{DelayMs}",
                operationName,
                jobId,
                _tableName,
                chunkNumber,
                unprocessedCount,
                retryAttempt,
                delay.TotalMilliseconds);

            Thread.Sleep(delay);
            remaining = new BatchWriteItemRequest { RequestItems = response.UnprocessedItems };
        }
    }

    private static bool IsParquetAvailable(IngestionJobAudit? audit)
        => audit != null
           && string.Equals(audit.WorkingFileFormat, "Parquet", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(audit.WorkingFilePath)
           && audit.WorkingFileRecordCount > 0;
}
