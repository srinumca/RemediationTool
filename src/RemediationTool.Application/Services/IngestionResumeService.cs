using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Exceptions;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemediationTool.Application.Services;

/// <summary>
/// Resumes failed ingestion jobs from their latest checkpoint without
/// reprocessing records that were already persisted successfully.
/// </summary>
public sealed class IngestionResumeService : IIngestionResumeService
{
    private readonly ILogger<IngestionResumeService> _logger;
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IngestionProcessingOptions _processingOptions;
    private readonly IIngestionCheckpointRepository _checkpointRepository;
    private readonly IIngestionStagingRepository _stagingRepository;
    private readonly IIngestionWorkingFileStrategy _workingFileStrategy;
    private readonly IAuditLogger _auditLogger;
    private readonly ResiliencePipeline _batchPersistencePipeline;
    private readonly AsyncLocal<ResumeBatchRetryState?> _retryState = new();

    public IngestionResumeService(
        ILogger<IngestionResumeService> logger,
        IFileFindingRepository repository,
        IStorageService storage,
        IIngestionJobAuditRepository jobAuditRepository,
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
        _processingOptions = processingOptions.Value;
        _checkpointRepository = checkpointRepository;
        _stagingRepository = stagingRepository;
        _workingFileStrategy = workingFileStrategy;
        _auditLogger = auditLogger;

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
                    var state = _retryState.Value;
                    if (state == null)
                        return default;

                    state.Response.BatchPersistenceRetryCount++;
                    state.JobAudit.BatchPersistenceRetryCount =
                        state.Response.BatchPersistenceRetryCount;

                    if (_processingOptions.EnableBatchCheckpointing)
                        _jobAuditRepository.Update(state.JobAudit);

                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "[INGESTION_RESUME_BATCH_RETRY] JobId:{JobId}, BatchNumber:{BatchNumber}, TotalBatches:{TotalBatches}, Attempt:{AttemptNumber}, Delay:{RetryDelay}",
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

    public async Task<IngestionUploadResponse> ResumeAsync(
        string reportUid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(reportUid))
            throw new ArgumentException("ReportUID is required.", nameof(reportUid));

        var checkpoint = _checkpointRepository.GetByJobId(reportUid)
            ?? throw new KeyNotFoundException(
                $"No checkpoint found for ReportUID '{reportUid}'.");

        if (!checkpoint.IsResumeEligible)
            return BuildNotEligibleResponse(checkpoint);

        var response = BuildResumeResponse(checkpoint);
        var jobAudit = BuildResumeJobAudit(reportUid, checkpoint, response);

        try
        {
            _logger.LogInformation(
                "[INGESTION_RESUME_START] JobId:{JobId}, LastSuccessfulBatch:{LastSuccessfulBatch}, LastProcessedRecordCount:{LastProcessedRecordCount}, TotalBatches:{TotalBatches}",
                reportUid,
                checkpoint.LastSuccessfulBatchNumber,
                checkpoint.LastProcessedRecordCount,
                checkpoint.TotalBatches);

            List<FileFinding> recordsToResume;
            try
            {
                recordsToResume = await LoadRecordsForResumeAsync(
                    reportUid,
                    checkpoint,
                    response,
                    jobAudit,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.Status = IngestionJobStatus.Failed;
                response.CompletedAtUtc = DateTime.UtcNow;
                response.Message = ex.Message;
                response.IsResumeEligible = false;
                response.CheckpointMessage =
                    "No readable Parquet working file or staged records were available for resume.";

                UpdateCheckpoint(
                    response,
                    jobAudit,
                    IngestionJobStatus.Failed,
                    ex.Message,
                    isResumeEligible: false);
                UpdateJobAudit(jobAudit, response, ex.Message);
                await TryStoreProcessingSummaryAsync(response, cancellationToken);

                throw new IngestionResumeDataUnavailableException(response, ex);
            }

            if (recordsToResume.Count == 0)
            {
                CompleteSuccessfulResume(response);
                response.Message =
                    "No remaining records were found. The ingestion job is already complete.";
                response.CheckpointMessage = "Resume completed. No pending records.";

                UpdateCheckpoint(
                    response,
                    jobAudit,
                    response.Status,
                    isResumeEligible: false);
                await StoreAndApplySummaryAsync(response, cancellationToken);
                UpdateJobAudit(jobAudit, response);
                await CleanupStagingAsync(response.JobId, cancellationToken);
                RecordCompletionAudit(response, jobAudit);
                return response;
            }

            await PersistRemainingFindingsInBatchesAsync(
                recordsToResume,
                response,
                jobAudit,
                response.BatchSize,
                checkpoint.LastSuccessfulBatchNumber,
                cancellationToken);

            CompleteSuccessfulResume(response);
            response.Message = "Ingestion resume completed successfully.";
            response.CheckpointMessage = "Resume completed successfully.";

            UpdateCheckpoint(
                response,
                jobAudit,
                response.Status,
                isResumeEligible: false);
            await StoreAndApplySummaryAsync(response, cancellationToken);
            UpdateJobAudit(jobAudit, response);
            await CleanupStagingAsync(response.JobId, cancellationToken);
            RecordCompletionAudit(response, jobAudit);

            _logger.LogInformation(
                "[INGESTION_RESUME_COMPLETE] JobId:{JobId}, Status:{Status}, LastSuccessfulBatch:{LastSuccessfulBatch}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                reportUid,
                response.Status,
                response.LastSuccessfulBatchNumber,
                response.LastProcessedRecordCount);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[INGESTION_RESUME_CANCELLED] JobId:{JobId}",
                reportUid);
            throw;
        }
        catch (IngestionResumeDataUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[INGESTION_RESUME_ERROR] JobId:{JobId}",
                reportUid);

            response.Status = IngestionJobStatus.Failed;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = $"Resume ingestion failed: {ex.Message}";
            response.IsResumeEligible =
                response.LastProcessedRecordCount < response.SuccessCount;
            response.CheckpointMessage = response.IsResumeEligible
                ? $"Resume can continue from record {response.LastProcessedRecordCount + 1}."
                : "Resume failed.";

            UpdateCheckpoint(
                response,
                jobAudit,
                IngestionJobStatus.Failed,
                ex.Message,
                response.IsResumeEligible);
            UpdateJobAudit(jobAudit, response, ex.Message);
            await TryStoreProcessingSummaryAsync(response, cancellationToken);
            return response;
        }
    }

    private async Task<List<FileFinding>> LoadRecordsForResumeAsync(
        string jobId,
        IngestionCheckpoint checkpoint,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        CancellationToken cancellationToken)
    {
        var workingFilePath = FirstNotEmpty(
            checkpoint.WorkingFilePath,
            response.WorkingFilePath,
            jobAudit.WorkingFilePath);
        var workingFileFormat = FirstNotEmpty(
            checkpoint.WorkingFileFormat,
            response.WorkingFileFormat,
            jobAudit.WorkingFileFormat);
        var workingFileRecordCount = Math.Max(
            checkpoint.WorkingFileRecordCount,
            Math.Max(response.WorkingFileRecordCount, jobAudit.WorkingFileRecordCount));

        if (_processingOptions.EnableParquetWorkingFile
            && string.Equals(
                workingFileFormat,
                _workingFileStrategy.Format,
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(workingFilePath)
            && workingFileRecordCount > 0)
        {
            try
            {
                _logger.LogInformation(
                    "[PARQUET_RESUME_PREFERRED] JobId:{JobId}, Path:{Path}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                    jobId,
                    workingFilePath,
                    checkpoint.LastProcessedRecordCount);

                var records = await _workingFileStrategy.ReadAfterAsync(
                    workingFilePath,
                    checkpoint.LastProcessedRecordCount,
                    cancellationToken);

                response.WorkingFilePath = workingFilePath;
                response.WorkingFileFormat = workingFileFormat;
                response.WorkingFileRecordCount = workingFileRecordCount;

                if (records.Count > 0
                    || checkpoint.LastProcessedRecordCount >= workingFileRecordCount)
                {
                    return records;
                }

                _logger.LogWarning(
                    "[PARQUET_RESUME_EMPTY_FALLBACK] JobId:{JobId}, Path:{Path}",
                    jobId,
                    workingFilePath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[PARQUET_RESUME_FAILED_FALLBACK] JobId:{JobId}, Path:{Path}",
                    jobId,
                    workingFilePath);
            }
        }

        _logger.LogInformation(
            "[STAGING_RESUME_FALLBACK] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            jobId,
            checkpoint.LastProcessedRecordCount);

        var stagedCount = await IngestionAsyncIo.CountStagedAsync(
            _stagingRepository,
            jobId,
            _processingOptions,
            cancellationToken);

        if (stagedCount == 0)
        {
            throw new InvalidOperationException(
                "Resume failed. Neither a readable Parquet working file nor staged records were found for this ReportUID. Re-upload may be required.");
        }

        var stagedRecords = await IngestionAsyncIo.ReadStagedAfterAsync(
            _stagingRepository,
            jobId,
            checkpoint.LastProcessedRecordCount,
            _processingOptions,
            cancellationToken);

        if (stagedRecords.Count == 0
            && checkpoint.LastProcessedRecordCount < checkpoint.SuccessCount)
        {
            throw new InvalidOperationException(
                "Resume failed. Staging exists, but no unprocessed records were available after the saved checkpoint.");
        }

        return stagedRecords;
    }

    private async Task PersistRemainingFindingsInBatchesAsync(
        List<FileFinding> remainingFindings,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        int batchSize,
        int lastSuccessfulBatchNumber,
        CancellationToken cancellationToken)
    {
        if (remainingFindings.Count == 0)
            return;

        var batchNumber = lastSuccessfulBatchNumber;
        foreach (var chunk in remainingFindings.Chunk(batchSize))
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
                    UpdateCheckpoint(
                        response,
                        jobAudit,
                        IngestionJobStatus.Started,
                        isResumeEligible: false);

                    if (ShouldPersistJobAuditProgress(
                        batchNumber,
                        response.TotalBatches))
                    {
                        _jobAuditRepository.Update(jobAudit);
                    }
                }

                _logger.LogInformation(
                    "[INGESTION_RESUME_BATCH_COMPLETE] JobId:{JobId}, BatchNumber:{BatchNumber}, TotalBatches:{TotalBatches}, RecordsPersisted:{RecordsPersisted}",
                    response.JobId,
                    batchNumber,
                    response.TotalBatches,
                    records.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                UpdateCheckpoint(
                    response,
                    jobAudit,
                    IngestionJobStatus.Failed,
                    ex.Message,
                    isResumeEligible: true);
                _jobAuditRepository.Update(jobAudit);

                throw new InvalidOperationException(
                    $"Resume batch persistence failed at batch {batchNumber} of {response.TotalBatches}. Last successful batch: {response.LastSuccessfulBatchNumber}.",
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
        var previousState = _retryState.Value;
        _retryState.Value = new ResumeBatchRetryState(
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
            _retryState.Value = previousState;
        }
    }

    private IngestionUploadResponse BuildResumeResponse(
        IngestionCheckpoint checkpoint)
    {
        var batchSize = checkpoint.BatchSize > 0
            ? checkpoint.BatchSize
            : ResolveBatchSize();
        var totalBatches = checkpoint.TotalBatches > 0
            ? checkpoint.TotalBatches
            : CalculateBatchCount(checkpoint.SuccessCount, batchSize);

        return new IngestionUploadResponse
        {
            ReportUid = checkpoint.JobId,
            JobId = checkpoint.JobId,
            InboundFileName = checkpoint.InboundFileName,
            SourceSystem = checkpoint.SourceSystem,
            TriggerType = "Resume",
            IngestionMode = checkpoint.IngestionMode,
            StartedAtUtc = DateTime.UtcNow,
            Status = IngestionJobStatus.Started,
            BatchSize = batchSize,
            TotalBatches = totalBatches,
            PersistedBatchCount = checkpoint.PersistedBatchCount,
            LastSuccessfulBatchNumber = checkpoint.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = checkpoint.LastProcessedRecordCount,
            SuccessCount = checkpoint.SuccessCount,
            RejectCount = checkpoint.RejectCount,
            ValidationFailureCount = checkpoint.RejectCount,
            TotalRecords = checkpoint.SuccessCount + checkpoint.RejectCount,
            PayloadRecordCount = checkpoint.SuccessCount + checkpoint.RejectCount,
            BatchPersistenceRetryCount = checkpoint.BatchPersistenceRetryCount,
            MaxBatchPersistenceRetryCount =
                _processingOptions.MaxBatchPersistenceRetryCount,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            IsResumeEligible = checkpoint.IsResumeEligible,
            LastCheckpointUtc = checkpoint.LastCheckpointUtc,
            CheckpointMessage =
                $"Resume started from batch {checkpoint.LastSuccessfulBatchNumber + 1}.",
            WorkingFilePath = checkpoint.WorkingFilePath,
            WorkingFileFormat = checkpoint.WorkingFileFormat,
            WorkingFileRecordCount = checkpoint.WorkingFileRecordCount
        };
    }

    private static IngestionUploadResponse BuildNotEligibleResponse(
        IngestionCheckpoint checkpoint)
    {
        return new IngestionUploadResponse
        {
            ReportUid = checkpoint.JobId,
            JobId = checkpoint.JobId,
            InboundFileName = checkpoint.InboundFileName,
            SourceSystem = checkpoint.SourceSystem,
            TriggerType = "Resume",
            IngestionMode = checkpoint.IngestionMode,
            Status = checkpoint.Status,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            BatchSize = checkpoint.BatchSize,
            TotalBatches = checkpoint.TotalBatches,
            PersistedBatchCount = checkpoint.PersistedBatchCount,
            LastSuccessfulBatchNumber = checkpoint.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = checkpoint.LastProcessedRecordCount,
            SuccessCount = checkpoint.SuccessCount,
            RejectCount = checkpoint.RejectCount,
            ValidationFailureCount = checkpoint.RejectCount,
            TotalRecords = checkpoint.SuccessCount + checkpoint.RejectCount,
            PayloadRecordCount = checkpoint.SuccessCount + checkpoint.RejectCount,
            BatchPersistenceRetryCount = checkpoint.BatchPersistenceRetryCount,
            IsResumeEligible = false,
            LastCheckpointUtc = checkpoint.LastCheckpointUtc,
            CheckpointMessage = checkpoint.FailureReason,
            WorkingFilePath = checkpoint.WorkingFilePath,
            WorkingFileFormat = checkpoint.WorkingFileFormat,
            WorkingFileRecordCount = checkpoint.WorkingFileRecordCount,
            Message = "This ingestion job is not eligible for resume."
        };
    }

    private IngestionJobAudit BuildResumeJobAudit(
        string jobId,
        IngestionCheckpoint checkpoint,
        IngestionUploadResponse response)
    {
        var audit = _jobAuditRepository.GetByJobId(jobId);
        var isNewAudit = audit == null;

        audit ??= new IngestionJobAudit
        {
            ReportUid = jobId,
            JobId = jobId,
            InboundFileName = checkpoint.InboundFileName,
            UploadedBy = checkpoint.UserName,
            UserName = checkpoint.UserName,
            StartedBy = checkpoint.UserName
        };

        response.S3FolderPath = audit.S3FolderPath;
        response.SourceFilePath = audit.SourceFilePath;
        response.ArchivedFilePath = audit.ArchivedFilePath ?? audit.SourceFilePath;
        response.MetadataJsonPath = audit.MetadataJsonPath;
        response.ProcessingSummaryPath =
            audit.ProcessingSummaryPath ?? audit.MetadataJsonPath;
        response.FindingTypeCounts = audit.FindingTypeCounts;
        response.WorkingFilePath ??= audit.WorkingFilePath;
        response.WorkingFileFormat ??= audit.WorkingFileFormat;
        response.WorkingFileRecordCount = Math.Max(
            response.WorkingFileRecordCount,
            audit.WorkingFileRecordCount);

        audit.StartTimestampUtc = response.StartedAtUtc;
        audit.TriggerType = "Resume";
        audit.IngestionMode = checkpoint.IngestionMode;
        audit.BatchSize = response.BatchSize;
        audit.TotalBatches = response.TotalBatches;
        audit.PersistedBatchCount = checkpoint.PersistedBatchCount;
        audit.LastSuccessfulBatchNumber = checkpoint.LastSuccessfulBatchNumber;
        audit.LastProcessedRecordCount = checkpoint.LastProcessedRecordCount;
        audit.SuccessCount = checkpoint.SuccessCount;
        audit.RejectCount = checkpoint.RejectCount;
        audit.TotalRecords = checkpoint.SuccessCount + checkpoint.RejectCount;
        audit.PayloadRecordCount = audit.TotalRecords;
        audit.ValidationFailureCount = checkpoint.RejectCount;
        audit.BatchPersistenceRetryCount = checkpoint.BatchPersistenceRetryCount;
        audit.MaxBatchPersistenceRetryCount =
            _processingOptions.MaxBatchPersistenceRetryCount;
        audit.CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing;
        audit.Status = IngestionJobStatus.Started;
        audit.IsResumeEligible = checkpoint.IsResumeEligible;
        audit.LastCheckpointUtc = checkpoint.LastCheckpointUtc;
        audit.CheckpointMessage = response.CheckpointMessage;
        audit.WorkingFilePath = response.WorkingFilePath;
        audit.WorkingFileFormat = response.WorkingFileFormat;
        audit.WorkingFileRecordCount = response.WorkingFileRecordCount;

        if (isNewAudit)
            _jobAuditRepository.Add(audit);
        else
            _jobAuditRepository.Update(audit);

        return audit;
    }

    private static IngestionCheckpoint BuildCheckpoint(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IngestionJobStatus status,
        string? failureReason,
        bool? isResumeEligible)
    {
        var eligible = isResumeEligible
            ?? (status == IngestionJobStatus.Failed
                && response.SuccessCount > 0
                && response.LastProcessedRecordCount < response.SuccessCount);

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
            IsResumeEligible = eligible,
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
        string? failureReason = null,
        bool? isResumeEligible = null)
    {
        if (!_processingOptions.EnableBatchCheckpointing)
            return;

        var checkpoint = BuildCheckpoint(
            response,
            jobAudit,
            status,
            failureReason,
            isResumeEligible);
        _checkpointRepository.Upsert(checkpoint);

        response.IsResumeEligible = checkpoint.IsResumeEligible;
        response.LastCheckpointUtc = checkpoint.LastCheckpointUtc;

        if (checkpoint.IsResumeEligible)
        {
            response.CheckpointMessage =
                $"Resume can continue from record {checkpoint.LastProcessedRecordCount + 1}.";
        }

        jobAudit.IsResumeEligible = response.IsResumeEligible;
        jobAudit.LastCheckpointUtc = response.LastCheckpointUtc;
        jobAudit.CheckpointMessage = response.CheckpointMessage;
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
        audit.MaxBatchPersistenceRetryCount =
            response.MaxBatchPersistenceRetryCount;
        audit.IsResumeEligible = response.IsResumeEligible;
        audit.LastCheckpointUtc = response.LastCheckpointUtc;
        audit.CheckpointMessage = response.CheckpointMessage;
        audit.Status = response.Status;
        audit.ErrorMessage = errorMessage;
        audit.FailureReason = errorMessage;
        audit.SourceFilePath = response.SourceFilePath ?? audit.SourceFilePath;
        audit.MetadataJsonPath = response.MetadataJsonPath ?? audit.MetadataJsonPath;
        audit.ArchivedFilePath =
            response.ArchivedFilePath ?? response.SourceFilePath;
        audit.ProcessingSummaryPath =
            response.ProcessingSummaryPath ?? response.MetadataJsonPath;
        audit.WorkingFilePath = response.WorkingFilePath;
        audit.WorkingFileFormat = response.WorkingFileFormat;
        audit.WorkingFileRecordCount = response.WorkingFileRecordCount;

        if (response.FindingTypeCounts.Count > 0)
            audit.FindingTypeCounts = response.FindingTypeCounts;

        _jobAuditRepository.Update(audit);
    }

    private async Task StoreAndApplySummaryAsync(
        IngestionUploadResponse response,
        CancellationToken cancellationToken)
    {
        var path = await StoreProcessingSummaryAsync(response, cancellationToken);
        response.MetadataJsonPath = path;
        response.ProcessingSummaryPath = path;
    }

    private async Task<string> StoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        CancellationToken cancellationToken)
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
            MaxBatchPersistenceRetryCount =
                response.MaxBatchPersistenceRetryCount,
            WorkingFileFormat = response.WorkingFileFormat,
            WorkingFilePath = response.WorkingFilePath,
            WorkingFileRecordCount = response.WorkingFileRecordCount,
            IsResumeEligible = response.IsResumeEligible,
            LastCheckpointUtc = response.LastCheckpointUtc,
            CheckpointMessage = response.CheckpointMessage,
            FinalJobStatus = response.Status,
            Message = response.Message,
            ArchivedFilePath = response.ArchivedFilePath
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

    private async Task TryStoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await StoreAndApplySummaryAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[PROCESSING_SUMMARY_WRITE_FAILED] JobId:{JobId}",
                response.JobId);
        }
    }

    private async Task CleanupStagingAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            await IngestionAsyncIo.DeleteStagingAsync(
                _stagingRepository,
                jobId,
                _processingOptions,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[STAGING_CLEANUP_FAILED] JobId:{JobId}",
                jobId);
        }
    }

    private void RecordCompletionAudit(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
    {
        _auditLogger.RecordEvent(
            eventType: "IngestionJobResumed",
            entityId: response.JobId,
            actor: jobAudit.UploadedBy ?? "system",
            outcome: response.Status.ToString(),
            details: new
            {
                response.TotalRecords,
                response.SuccessCount,
                response.RejectCount,
                response.LastProcessedRecordCount,
                response.WorkingFileFormat,
                response.WorkingFileRecordCount
            });
    }

    private static void CompleteSuccessfulResume(
        IngestionUploadResponse response)
    {
        response.Status = response.RejectCount > 0
            ? IngestionJobStatus.PartialSuccess
            : IngestionJobStatus.Success;
        response.CompletedAtUtc = DateTime.UtcNow;
        response.IsResumeEligible = false;
    }

    private static void CopyBatchProgressToAudit(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
    {
        jobAudit.TotalBatches = response.TotalBatches;
        jobAudit.PersistedBatchCount = response.PersistedBatchCount;
        jobAudit.LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber;
        jobAudit.LastProcessedRecordCount = response.LastProcessedRecordCount;
        jobAudit.BatchPersistenceRetryCount =
            response.BatchPersistenceRetryCount;
    }

    private bool ShouldPersistJobAuditProgress(
        int batchNumber,
        int totalBatches)
    {
        var interval = Math.Max(
            1,
            _processingOptions.JobAuditProgressUpdateIntervalBatches);

        return interval == 1
            || batchNumber == totalBatches
            || batchNumber % interval == 0;
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

    private static string? FirstNotEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record ResumeBatchRetryState(
        IngestionUploadResponse Response,
        IngestionJobAudit JobAudit,
        int BatchNumber,
        int TotalBatches);
}
