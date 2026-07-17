using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemediationTool.Application.Services;

public class IngestionService
{
    private readonly ILogger<IngestionService> _logger;
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IRejectedRowRepository _rejectedRowRepository;
    private readonly IngestionProcessingOptions _processingOptions;
    private readonly IIngestionCheckpointRepository _checkpointRepository;
    private readonly IIngestionStagingRepository _stagingRepository;
    private readonly IIngestionWorkingFileStrategy _workingFileStrategy;
    private readonly IAuditLogger _auditLogger;
    private readonly InboundFileParser _fileParser;
    private readonly ResiliencePipeline _batchPersistencePipeline;
    private readonly AsyncLocal<BatchPersistenceRetryState?> _batchPersistenceRetryState = new();

    public IngestionService(
        ILogger<IngestionService> logger,
        IFileFindingRepository repository,
        IStorageService storage,
        IValidator<FileFinding> validator,
        IIngestionJobAuditRepository jobAuditRepository,
        IRejectedRowRepository rejectedRowRepository,
        IOptions<IngestionProcessingOptions> processingOptions,
        IIngestionCheckpointRepository checkpointRepository,
        IIngestionStagingRepository stagingRepository,
        IIngestionWorkingFileStrategy workingFileStrategy,
        IAuditLogger auditLogger)
    {
        _logger = logger;
        _repository = repository;
        _storage = storage;
        _jobAuditRepository = jobAuditRepository;
        _rejectedRowRepository = rejectedRowRepository;
        _processingOptions = processingOptions.Value;
        _checkpointRepository = checkpointRepository;
        _stagingRepository = stagingRepository;
        _workingFileStrategy = workingFileStrategy;
        _auditLogger = auditLogger;
        _fileParser = new InboundFileParser(validator, logger, _processingOptions);
        _batchPersistencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, _processingOptions.MaxBatchPersistenceRetryCount),
                Delay = TimeSpan.FromMilliseconds(
                    Math.Max(0, _processingOptions.BatchPersistenceRetryDelayMilliseconds)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<IOException>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(),
                OnRetry = args =>
                {
                    var state = _batchPersistenceRetryState.Value;
                    if (state == null)
                        return default;

                    state.Response.BatchPersistenceRetryCount++;
                    state.JobAudit.BatchPersistenceRetryCount = state.Response.BatchPersistenceRetryCount;

                    if (_processingOptions.EnableBatchCheckpointing)
                        _jobAuditRepository.Update(state.JobAudit);

                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "[INGESTION_BATCH_RETRY] JobId:{JobId}, BatchNumber:{BatchNumber}, TotalBatches:{TotalBatches}, Attempt:{AttemptNumber}, Delay:{RetryDelay}",
                        state.Response.JobId,
                        state.BatchNumber,
                        state.TotalBatches,
                        args.AttemptNumber + 1,
                        args.RetryDelay);
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Processes a source file that was previously uploaded by the upload API.
    /// </summary>
    public async Task<IngestionUploadResponse> IngestAsync(
        string reportUid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(reportUid))
            throw new ArgumentException("ReportUID is required.", nameof(reportUid));

        var jobAudit = _jobAuditRepository.GetByJobId(reportUid)
            ?? throw new KeyNotFoundException($"No report record found for ReportUID '{reportUid}'.");

        var uploadedBy = jobAudit.UploadedBy ?? "system";
        var loadTime = DateTime.UtcNow;
        var extension = Path.GetExtension(jobAudit.InboundFileName).ToLowerInvariant();
        var configuredBatchSize = ResolveBatchSize();

        var response = CreateInitialResponse(
            reportUid,
            jobAudit.InboundFileName,
            jobAudit.S3FolderPath,
            DateTime.UtcNow,
            triggerType: "StepFunction",
            configuredBatchSize);
        response.SourceFilePath = jobAudit.SourceFilePath;
        response.ArchivedFilePath = jobAudit.SourceFilePath;

        jobAudit.Status = IngestionJobStatus.Started;
        jobAudit.TriggerType = "StepFunction";
        jobAudit.StartTimestampUtc = response.StartedAtUtc;
        jobAudit.BatchSize = configuredBatchSize;
        jobAudit.CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing;
        jobAudit.MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount;
        _jobAuditRepository.Update(jobAudit);
        _checkpointRepository.Upsert(BuildCheckpoint(response, jobAudit, IngestionJobStatus.Started));

        InboundParseResult? parseResult = null;

        try
        {
            _logger.LogInformation(
                "[INGEST_ASYNC_START] ReportUid:{ReportUid}, File:{File}",
                reportUid,
                jobAudit.InboundFileName);

            var sourceDownloadStopwatch = Stopwatch.StartNew();
            await using var fileStream = await IngestionAsyncIo.OpenSourceReadAsync(
                _storage,
                _processingOptions,
                jobAudit.SourceFilePath,
                extension,
                cancellationToken);
            LogStageDuration(reportUid, "SourceDownload", sourceDownloadStopwatch, jobAudit.FileSizeBytes);

            var parseStopwatch = Stopwatch.StartNew();
            parseResult = _fileParser.Parse(
                fileStream,
                extension,
                reportUid,
                jobAudit.InboundFileName,
                uploadedBy,
                loadTime,
                cancellationToken);
            LogStageDuration(reportUid, "ParseAndValidate", parseStopwatch, parseResult.TotalRecords);

            ApplyParseResult(response, parseResult);

            var rejectedRowsStopwatch = Stopwatch.StartNew();
            await PersistRejectedRowsAsync(
                reportUid,
                jobAudit.InboundFileName,
                parseResult.RejectedRows,
                cancellationToken);
            LogStageDuration(
                reportUid,
                "RejectedRowPersistence",
                rejectedRowsStopwatch,
                parseResult.RejectedRows.Count);

            var workingStoreStopwatch = Stopwatch.StartNew();
            var stagingWritten = await PrepareWorkingStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings,
                cancellationToken);
            LogStageDuration(
                reportUid,
                "WorkingStorePreparation",
                workingStoreStopwatch,
                parseResult.ValidFindings.Count);

            var targetPersistenceStopwatch = Stopwatch.StartNew();
            await PersistValidFindingsInBatchesAsync(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize,
                cancellationToken);
            LogStageDuration(
                reportUid,
                "TargetPersistence",
                targetPersistenceStopwatch,
                parseResult.ValidFindings.Count);

            CompleteResponse(response);
            UpdateCheckpoint(response, jobAudit, response.Status);

            var summaryStopwatch = Stopwatch.StartNew();
            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows,
                cancellationToken);
            response.MetadataJsonPath = storedMetadataPath;
            response.ProcessingSummaryPath = storedMetadataPath;
            LogStageDuration(
                reportUid,
                "ProcessingSummary",
                summaryStopwatch,
                parseResult.RejectedRows.Count);

            UpdateJobAudit(jobAudit, response);
            if (stagingWritten)
            {
                var cleanupStopwatch = Stopwatch.StartNew();
                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
                LogStageDuration(
                    reportUid,
                    "StagingCleanup",
                    cleanupStopwatch,
                    parseResult.ValidFindings.Count);
            }
            else
            {
                _logger.LogInformation(
                    "[STAGING_CLEANUP_SKIPPED] JobId:{JobId}, Reason:{Reason}",
                    reportUid,
                    "No staging records were written because verified Parquet is the primary working store.");
            }

            RecordCompletionAudit(response, jobAudit);

            _logger.LogInformation(
                "[INGEST_ASYNC_COMPLETE] ReportUid:{ReportUid}, Status:{Status}, Total:{Total}, Success:{Success}, Rejected:{Rejected}, WorkingFile:{WorkingFile}",
                reportUid,
                response.Status,
                response.TotalRecords,
                response.SuccessCount,
                response.RejectCount,
                response.WorkingFilePath);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[INGEST_ASYNC_CANCELLED] ReportUid:{ReportUid}", reportUid);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGEST_ASYNC_ERROR] ReportUid:{ReportUid}", reportUid);

            response.Status = IngestionJobStatus.Failed;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = $"Ingestion failed: {ex.Message}";
            response.SourceSystem ??= "Unknown";

            TryUpdateFailureState(response, jobAudit, ex.Message);
            await TryStoreProcessingSummaryAsync(
                response,
                parseResult?.RejectedRows,
                cancellationToken);
            return response;
        }
    }

    private IngestionUploadResponse CreateInitialResponse(
        string reportUid,
        string inboundFileName,
        string s3FolderPath,
        DateTime startedAtUtc,
        string triggerType,
        int batchSize)
    {
        return new IngestionUploadResponse
        {
            ReportUid = reportUid,
            JobId = reportUid,
            InboundFileName = inboundFileName,
            S3FolderPath = s3FolderPath,
            StartedAtUtc = startedAtUtc,
            Status = IngestionJobStatus.Started,
            TriggerType = triggerType,
            IngestionMode = "Full",
            BatchSize = batchSize,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount
        };
    }

    private static void ApplyParseResult(
        IngestionUploadResponse response,
        InboundParseResult parseResult)
    {
        response.SuccessCount = parseResult.SuccessCount;
        response.RejectCount = parseResult.RejectedRecordCount;
        response.TotalRecords = parseResult.TotalRecords;
        response.PayloadRecordCount = parseResult.TotalRecords;
        response.ValidationFailureCount = parseResult.RejectedRecordCount;
        response.SourceSystem = parseResult.SourceSystem;
        response.FindingTypeCounts = new Dictionary<string, int>(
            parseResult.FindingTypeCounts,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task CreateWorkingFileAsync(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken)
    {
        if (!_processingOptions.EnableParquetWorkingFile || validFindings.Count == 0)
            return;

        var result = await _workingFileStrategy.WriteAsync(
            response.JobId,
            response.InboundFileName,
            validFindings,
            cancellationToken);

        response.WorkingFileFormat = result.Format;
        response.WorkingFilePath = result.Path;
        response.WorkingFileRecordCount = result.RecordCount;

        jobAudit.WorkingFileFormat = result.Format;
        jobAudit.WorkingFilePath = result.Path;
        jobAudit.WorkingFileRecordCount = result.RecordCount;
        _jobAuditRepository.Update(jobAudit);

        UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
    }

    private async Task<bool> PrepareWorkingStoreAsync(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken)
    {
        var result = await IngestionResumeStoreCoordinator.PrepareAsync(
            _processingOptions,
            validFindings.Count,
            createParquetAsync: token => CreateWorkingFileAsync(
                response,
                jobAudit,
                validFindings,
                token),
            writeStagingAsync: token => IngestionAsyncIo.SaveStagingAsync(
                _stagingRepository,
                response.JobId,
                validFindings,
                _processingOptions,
                token),
            clearParquetMetadata: () => ClearWorkingFileMetadata(response, jobAudit),
            cancellationToken);

        if (result.ParquetFailure != null)
        {
            _logger.LogWarning(
                result.ParquetFailure,
                "[PARQUET_PRIMARY_FAILED_STAGING_FALLBACK] JobId:{JobId}, Records:{Records}",
                response.JobId,
                validFindings.Count);
        }

        if (result.StagingWritten)
        {
            UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
            if (_processingOptions.EnableBatchCheckpointing)
                _jobAuditRepository.Update(jobAudit);
        }
        else if (result.ParquetReady)
        {
            _logger.LogInformation(
                "[STAGING_WRITE_SKIPPED] JobId:{JobId}, Records:{Records}, WorkingStore:{WorkingStore}",
                response.JobId,
                validFindings.Count,
                _workingFileStrategy.Format);
        }

        return result.StagingWritten;
    }

    private static void ClearWorkingFileMetadata(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
    {
        response.WorkingFileFormat = null;
        response.WorkingFilePath = null;
        response.WorkingFileRecordCount = 0;
        jobAudit.WorkingFileFormat = null;
        jobAudit.WorkingFilePath = null;
        jobAudit.WorkingFileRecordCount = 0;
    }

    private async Task PersistValidFindingsInBatchesAsync(
        List<FileFinding> validFindings,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (validFindings.Count == 0)
        {
            response.TotalBatches = 0;
            response.PersistedBatchCount = 0;
            response.LastSuccessfulBatchNumber = 0;
            response.LastProcessedRecordCount = 0;
            CopyBatchProgressToAudit(response, jobAudit);
            _jobAuditRepository.Update(jobAudit);
            return;
        }

        response.TotalBatches = CalculateBatchCount(validFindings.Count, batchSize);
        jobAudit.TotalBatches = response.TotalBatches;
        _jobAuditRepository.Update(jobAudit);

        var batchNumber = 0;
        foreach (var chunk in validFindings.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchNumber++;
            IReadOnlyList<FileFinding> records = chunk;

            try
            {
                await PersistBatchWithRetryAsync(
                    records,
                    batchNumber,
                    response.TotalBatches,
                    response,
                    jobAudit,
                    cancellationToken);

                response.PersistedBatchCount++;
                response.LastSuccessfulBatchNumber = batchNumber;
                response.LastProcessedRecordCount += records.Count;
                CopyBatchProgressToAudit(response, jobAudit);

                if (_processingOptions.EnableBatchCheckpointing)
                {
                    UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
                    if (ShouldPersistJobAuditProgress(batchNumber, response.TotalBatches))
                        _jobAuditRepository.Update(jobAudit);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[INGESTION_BATCH_FAILED] JobId:{JobId}, BatchNumber:{BatchNumber}, TotalBatches:{TotalBatches}, LastSuccessfulBatch:{LastSuccessfulBatch}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                    response.JobId,
                    batchNumber,
                    response.TotalBatches,
                    response.LastSuccessfulBatchNumber,
                    response.LastProcessedRecordCount);

                UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, ex.Message);
                _jobAuditRepository.Update(jobAudit);

                throw new InvalidOperationException(
                    $"Batch persistence failed at batch {batchNumber} of {response.TotalBatches} after {_processingOptions.MaxBatchPersistenceRetryCount} retry attempt(s). Last successful batch: {response.LastSuccessfulBatchNumber}.",
                    ex);
            }
        }
    }

    private async Task PersistBatchWithRetryAsync(
        IReadOnlyList<FileFinding> records,
        int batchNumber,
        int totalBatches,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        CancellationToken cancellationToken)
    {
        var previousState = _batchPersistenceRetryState.Value;
        _batchPersistenceRetryState.Value = new BatchPersistenceRetryState(
            response,
            jobAudit,
            batchNumber,
            totalBatches);

        try
        {
            await _batchPersistencePipeline.ExecuteAsync(
                token => new ValueTask(IngestionAsyncIo.PersistFindingsAsync(
                    _repository,
                    records,
                    _processingOptions,
                    token)),
                cancellationToken);
        }
        finally
        {
            _batchPersistenceRetryState.Value = previousState;
        }
    }

    private static IngestionCheckpoint BuildCheckpoint(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IngestionJobStatus status,
        string? failureReason = null)
    {
        return new IngestionCheckpoint
        {
            JobId = response.JobId,
            InboundFileName = response.InboundFileName,
            UserName = jobAudit.UserName,
            SourceSystem = response.SourceSystem,
            TriggerType = response.TriggerType,
            IngestionMode = response.IngestionMode,
            BatchSize = response.BatchSize,
            TotalBatches = response.TotalBatches,
            LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = response.LastProcessedRecordCount,
            PersistedBatchCount = response.PersistedBatchCount,
            SuccessCount = response.SuccessCount,
            RejectCount = response.RejectCount,
            BatchPersistenceRetryCount = response.BatchPersistenceRetryCount,
            Status = status,
            IsResumeEligible = status == IngestionJobStatus.Failed
                && response.SuccessCount > 0
                && response.LastProcessedRecordCount < response.SuccessCount,
            LastCheckpointUtc = DateTime.UtcNow,
            FailureReason = failureReason,
            WorkingFilePath = response.WorkingFilePath,
            WorkingFileFormat = response.WorkingFileFormat,
            WorkingFileRecordCount = response.WorkingFileRecordCount
        };
    }

    private void UpdateCheckpoint(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IngestionJobStatus status,
        string? failureReason = null)
    {
        if (!_processingOptions.EnableBatchCheckpointing)
            return;

        var checkpoint = BuildCheckpoint(response, jobAudit, status, failureReason);
        _checkpointRepository.Upsert(checkpoint);

        response.IsResumeEligible = checkpoint.IsResumeEligible;
        response.LastCheckpointUtc = checkpoint.LastCheckpointUtc;
        response.CheckpointMessage = checkpoint.IsResumeEligible
            ? $"Processing stopped after record {checkpoint.LastProcessedRecordCount}."
            : "Checkpoint updated.";

        jobAudit.IsResumeEligible = response.IsResumeEligible;
        jobAudit.LastCheckpointUtc = response.LastCheckpointUtc;
        jobAudit.CheckpointMessage = response.CheckpointMessage;
    }

    private async Task<string> StoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        IReadOnlyCollection<RejectedRowSummary>? rejectedRows = null,
        CancellationToken cancellationToken = default)
    {
        var summary = new ProcessingSummaryArtifact
        {
            JobId = response.JobId,
            InboundFileName = response.InboundFileName,
            SourceSystem = response.SourceSystem,
            TriggerType = response.TriggerType,
            IngestionMode = response.IngestionMode,
            StartedAtUtc = response.StartedAtUtc,
            CompletedAtUtc = response.CompletedAtUtc,
            ProcessingStartTimeUtc = response.StartedAtUtc,
            ProcessingEndTimeUtc = response.CompletedAtUtc,
            PayloadRecordCount = response.PayloadRecordCount,
            TotalRowsProcessed = response.TotalRecords,
            SuccessfulRows = response.SuccessCount,
            FailedRows = response.RejectCount,
            ValidationFailureCount = response.ValidationFailureCount,
            BatchSize = response.BatchSize,
            TotalBatches = response.TotalBatches,
            PersistedBatchCount = response.PersistedBatchCount,
            LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = response.LastProcessedRecordCount,
            CheckpointingEnabled = response.CheckpointingEnabled,
            BatchPersistenceRetryCount = response.BatchPersistenceRetryCount,
            MaxBatchPersistenceRetryCount = response.MaxBatchPersistenceRetryCount,
            WorkingFileFormat = response.WorkingFileFormat,
            WorkingFilePath = response.WorkingFilePath,
            WorkingFileRecordCount = response.WorkingFileRecordCount,
            IsResumeEligible = response.IsResumeEligible,
            LastCheckpointUtc = response.LastCheckpointUtc,
            CheckpointMessage = response.CheckpointMessage,
            FinalJobStatus = response.Status,
            Message = response.Message,
            ArchivedFilePath = response.ArchivedFilePath,
            RejectedRows = rejectedRows?.ToList() ?? new List<RejectedRowSummary>()
        };

        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });

        var summaryKey = IngestionArchivePathBuilder.BuildProcessingSummaryPath(
            response.ReportUid,
            response.StartedAtUtc);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await _storage.UploadAsync(summaryKey, stream, cancellationToken);
        return summaryKey;
    }

    private async Task PersistRejectedRowsAsync(
        string jobId,
        string inboundFileName,
        IReadOnlyCollection<RejectedRowSummary> rejectedRows,
        CancellationToken cancellationToken)
    {
        if (rejectedRows.Count == 0)
            return;

        var batchSize = _processingOptions.ResolveRejectedRowBatchSize();
        var details = new List<RejectedRowDetail>(Math.Min(batchSize, rejectedRows.Count));

        foreach (var row in rejectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            details.Add(new RejectedRowDetail
            {
                RejectedRowId = string.IsNullOrWhiteSpace(row.RejectedRowId)
                    ? Guid.NewGuid().ToString("N")
                    : row.RejectedRowId,
                JobId = jobId,
                InboundFileName = inboundFileName,
                SourceRecordId = row.SourceRecordId,
                FindingFileName = row.FindingFileName,
                FindingType = row.FindingType,
                UserName = row.UserName,
                RowNumber = row.RowNumber,
                FieldName = row.FieldName,
                RejectedValue = row.RejectedValue,
                ErrorReason = row.ErrorReason,
                ErrorCategory = row.ErrorCategory,
                ErrorDateUtc = row.ErrorDateUtc == default ? DateTime.UtcNow : row.ErrorDateUtc,
                RawRowJson = row.RawRowJson
            });

            if (details.Count < batchSize)
                continue;

            await IngestionAsyncIo.PersistRejectedRowsAsync(
                _rejectedRowRepository,
                details,
                _processingOptions,
                cancellationToken);
            details.Clear();
        }

        if (details.Count > 0)
        {
            await IngestionAsyncIo.PersistRejectedRowsAsync(
                _rejectedRowRepository,
                details,
                _processingOptions,
                cancellationToken);
        }
    }

    private void UpdateJobAudit(
        IngestionJobAudit audit,
        IngestionUploadResponse response,
        string? errorMessage = null)
    {
        audit.ReportUid = response.ReportUid;
        audit.JobId = response.JobId;
        audit.EndTimestampUtc = response.CompletedAtUtc;
        audit.SourceSystem = response.SourceSystem;
        audit.TriggerType = response.TriggerType;
        audit.IngestionMode = response.IngestionMode;
        audit.PayloadRecordCount = response.PayloadRecordCount;
        audit.TotalRecords = response.TotalRecords;
        audit.SuccessCount = response.SuccessCount;
        audit.RejectCount = response.RejectCount;
        audit.ValidationFailureCount = response.ValidationFailureCount;
        audit.BatchSize = response.BatchSize;
        audit.TotalBatches = response.TotalBatches;
        audit.PersistedBatchCount = response.PersistedBatchCount;
        audit.LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber;
        audit.LastProcessedRecordCount = response.LastProcessedRecordCount;
        audit.CheckpointingEnabled = response.CheckpointingEnabled;
        audit.BatchPersistenceRetryCount = response.BatchPersistenceRetryCount;
        audit.MaxBatchPersistenceRetryCount = response.MaxBatchPersistenceRetryCount;
        audit.IsResumeEligible = response.IsResumeEligible;
        audit.LastCheckpointUtc = response.LastCheckpointUtc;
        audit.CheckpointMessage = response.CheckpointMessage;
        audit.Status = response.Status;
        audit.ErrorMessage = errorMessage;
        audit.FailureReason = errorMessage;
        audit.SourceFilePath = response.SourceFilePath ?? audit.SourceFilePath;
        audit.MetadataJsonPath = response.MetadataJsonPath ?? audit.MetadataJsonPath;
        audit.ArchivedFilePath = response.ArchivedFilePath ?? response.SourceFilePath;
        audit.ProcessingSummaryPath = response.ProcessingSummaryPath ?? response.MetadataJsonPath;
        audit.WorkingFilePath = response.WorkingFilePath;
        audit.WorkingFileFormat = response.WorkingFileFormat;
        audit.WorkingFileRecordCount = response.WorkingFileRecordCount;

        if (response.FindingTypeCounts.Count > 0)
            audit.FindingTypeCounts = response.FindingTypeCounts;

        _jobAuditRepository.Update(audit);
    }

    private void TryUpdateFailureState(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        string failureReason)
    {
        try
        {
            UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, failureReason);
        }
        catch (Exception checkpointEx)
        {
            _logger.LogError(
                checkpointEx,
                "[CHECKPOINT_UPDATE_FAILED] JobId:{JobId}",
                response.JobId);
        }

        try
        {
            UpdateJobAudit(jobAudit, response, failureReason);
        }
        catch (Exception auditEx)
        {
            _logger.LogError(
                auditEx,
                "[JOB_AUDIT_UPDATE_FAILED] JobId:{JobId}",
                response.JobId);
        }
    }

    private async Task TryStoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        IReadOnlyCollection<RejectedRowSummary>? rejectedRows = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await StoreProcessingSummaryAsync(
                response,
                rejectedRows,
                cancellationToken);
            response.MetadataJsonPath = path;
            response.ProcessingSummaryPath = path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception summaryEx)
        {
            _logger.LogError(
                summaryEx,
                "[PROCESSING_SUMMARY_WRITE_FAILED] JobId:{JobId}",
                response.JobId);
        }
    }

    private async Task CleanupStagingForCompletedJobAsync(
        IngestionUploadResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Status != IngestionJobStatus.Success
            && response.Status != IngestionJobStatus.PartialSuccess)
        {
            return;
        }

        try
        {
            await IngestionAsyncIo.DeleteStagingAsync(
                _stagingRepository,
                response.JobId,
                _processingOptions,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception cleanupEx)
        {
            _logger.LogWarning(
                cleanupEx,
                "[STAGING_CLEANUP_FAILED] JobId:{JobId}",
                response.JobId);
        }
    }

    private void RecordCompletionAudit(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
    {
        _auditLogger.RecordEvent(
            eventType: "IngestionJobCompleted",
            entityId: response.JobId,
            actor: jobAudit.UploadedBy ?? "system",
            outcome: response.Status.ToString(),
            details: new
            {
                response.TotalRecords,
                response.SuccessCount,
                response.RejectCount,
                response.WorkingFileFormat,
                response.WorkingFileRecordCount
            });
    }

    private void CompleteResponse(IngestionUploadResponse response)
    {
        response.Status = DetermineFinalStatus(response.SuccessCount, response.RejectCount);
        response.CompletedAtUtc = DateTime.UtcNow;
        response.Message = BuildResponseMessage(
            response.Status,
            response.SuccessCount,
            response.RejectCount);
    }

    private int ResolveBatchSize()
    {
        return Math.Clamp(
            _processingOptions.BatchSize,
            _processingOptions.MinBatchSize,
            _processingOptions.MaxBatchSize);
    }

    private static int CalculateBatchCount(int recordCount, int batchSize)
        => recordCount == 0 ? 0 : (recordCount + batchSize - 1) / batchSize;

    private static IngestionJobStatus DetermineFinalStatus(int successCount, int rejectCount)
    {
        if (successCount > 0 && rejectCount == 0)
            return IngestionJobStatus.Success;
        if (successCount > 0)
            return IngestionJobStatus.PartialSuccess;
        return IngestionJobStatus.Failed;
    }

    private static string BuildResponseMessage(
        IngestionJobStatus status,
        int successCount,
        int rejectCount)
    {
        return status switch
        {
            IngestionJobStatus.Success => "File processed successfully.",
            IngestionJobStatus.PartialSuccess =>
                $"File processed with partial success. Success: {successCount}, Rejected: {rejectCount}.",
            IngestionJobStatus.Failed => "File processing failed. No valid records were ingested.",
            _ => "File processing completed."
        };
    }

    private static void CopyBatchProgressToAudit(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
    {
        jobAudit.TotalBatches = response.TotalBatches;
        jobAudit.PersistedBatchCount = response.PersistedBatchCount;
        jobAudit.LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber;
        jobAudit.LastProcessedRecordCount = response.LastProcessedRecordCount;
        jobAudit.BatchPersistenceRetryCount = response.BatchPersistenceRetryCount;
    }

    private bool ShouldPersistJobAuditProgress(int batchNumber, int totalBatches)
    {
        var interval = Math.Max(1, _processingOptions.JobAuditProgressUpdateIntervalBatches);
        return interval == 1
            || batchNumber == totalBatches
            || batchNumber % interval == 0;
    }

    private void LogStageDuration(
        string jobId,
        string stage,
        Stopwatch stopwatch,
        long itemCount)
    {
        stopwatch.Stop();
        _logger.LogInformation(
            "[INGESTION_STAGE_COMPLETE] JobId:{JobId}, Stage:{Stage}, ElapsedMs:{ElapsedMs}, ItemCount:{ItemCount}",
            jobId,
            stage,
            stopwatch.ElapsedMilliseconds,
            itemCount);
    }

    private sealed record BatchPersistenceRetryState(
        IngestionUploadResponse Response,
        IngestionJobAudit JobAudit,
        int BatchNumber,
        int TotalBatches);
}
