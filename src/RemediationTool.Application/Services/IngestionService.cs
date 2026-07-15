using FluentValidation;
using Microsoft.AspNetCore.Http;
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

public class IngestionService : IIngestionService
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

    private static readonly string[] AllowedUploadExtensions = { ".csv", ".xlsx" };

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

    public async Task<IngestionUploadResponse> ProcessAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAtUtc = DateTime.UtcNow;
        var reportUid = IngestionJobIdGenerator.Generate();

        ValidateUploadedFile(file);
        ArgumentNullException.ThrowIfNull(file);

        var uploadedBy = "system";
        var loadTime = DateTime.UtcNow;
        var inboundFileName = file.FileName;
        var fileSizeBytes = file.Length;
        var fileFormat = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var configuredBatchSize = ResolveBatchSize();

        var s3FolderPath = IngestionArchivePathBuilder.BuildFolderPrefix(reportUid, startedAtUtc);
        var sourceFilePath = IngestionArchivePathBuilder.BuildOriginalFilePath(reportUid, inboundFileName, startedAtUtc);
        var metadataPath = IngestionArchivePathBuilder.BuildProcessingSummaryPath(reportUid, startedAtUtc);

        var response = CreateInitialResponse(
            reportUid,
            inboundFileName,
            s3FolderPath,
            startedAtUtc,
            triggerType: "Manual",
            configuredBatchSize);

        var jobAudit = new IngestionJobAudit
        {
            ReportUid = reportUid,
            JobId = reportUid,
            InboundFileName = inboundFileName,
            FileSizeBytes = fileSizeBytes,
            InboundFileContentType = file.ContentType,
            FileFormat = fileFormat,
            S3FolderPath = s3FolderPath,
            SourceFilePath = sourceFilePath,
            MetadataJsonPath = metadataPath,
            UploadedBy = uploadedBy,
            UserName = uploadedBy,
            StartedBy = uploadedBy,
            StartTimestampUtc = startedAtUtc,
            Status = IngestionJobStatus.Started,
            TriggerType = "Manual",
            IngestionMode = "Full",
            BatchSize = configuredBatchSize,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount
        };

        _jobAuditRepository.Add(jobAudit);
        _checkpointRepository.Upsert(BuildCheckpoint(response, jobAudit, IngestionJobStatus.Started));

        InboundParseResult? parseResult = null;

        try
        {
            _logger.LogInformation(
                "[INGESTION_START] ReportUid:{ReportUid}, File:{File}, SizeBytes:{SizeBytes}",
                reportUid,
                inboundFileName,
                fileSizeBytes);

            var sourceUploadStopwatch = Stopwatch.StartNew();
            await using (var sourceStream = file.OpenReadStream())
            {
                await _storage.UploadAsync(sourceFilePath, sourceStream, cancellationToken);
            }
            LogStageDuration(reportUid, "SourceUpload", sourceUploadStopwatch, fileSizeBytes);

            response.SourceFilePath = sourceFilePath;
            response.ArchivedFilePath = sourceFilePath;

            var parseStopwatch = Stopwatch.StartNew();
            using (var parseStream = file.OpenReadStream())
            {
                parseResult = _fileParser.Parse(
                    parseStream,
                    extension,
                    reportUid,
                    inboundFileName,
                    uploadedBy,
                    loadTime,
                    cancellationToken);
            }
            LogStageDuration(reportUid, "ParseAndValidate", parseStopwatch, parseResult.TotalRecords);

            ApplyParseResult(response, parseResult);

            var rejectedRowsStopwatch = Stopwatch.StartNew();
            await PersistRejectedRowsAsync(
                reportUid,
                inboundFileName,
                parseResult.RejectedRows,
                cancellationToken);
            LogStageDuration(reportUid, "RejectedRowPersistence", rejectedRowsStopwatch, parseResult.RejectedRows.Count);

            var resumeStoreStopwatch = Stopwatch.StartNew();
            var stagingWritten = await PrepareResumeStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings,
                cancellationToken);
            LogStageDuration(reportUid, "ResumeStorePreparation", resumeStoreStopwatch, parseResult.ValidFindings.Count);

            var targetPersistenceStopwatch = Stopwatch.StartNew();
            await PersistValidFindingsInBatchesAsync(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize,
                cancellationToken);
            LogStageDuration(reportUid, "TargetPersistence", targetPersistenceStopwatch, parseResult.ValidFindings.Count);

            CompleteResponse(response);
            UpdateCheckpoint(response, jobAudit, response.Status);

            var summaryStopwatch = Stopwatch.StartNew();
            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows,
                cancellationToken);
            response.MetadataJsonPath = storedMetadataPath;
            response.ProcessingSummaryPath = storedMetadataPath;
            LogStageDuration(reportUid, "ProcessingSummary", summaryStopwatch, parseResult.RejectedRows.Count);

            UpdateJobAudit(jobAudit, response);
            if (stagingWritten)
            {
                var cleanupStopwatch = Stopwatch.StartNew();
                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
                LogStageDuration(reportUid, "StagingCleanup", cleanupStopwatch, parseResult.ValidFindings.Count);
            }
            else
            {
                _logger.LogInformation(
                    "[STAGING_CLEANUP_SKIPPED] JobId:{JobId}, Reason:{Reason}",
                    reportUid,
                    "No staging records were written because verified Parquet is the primary resume store.");
            }
            RecordCompletionAudit(response, jobAudit);

            _logger.LogInformation(
                "[INGESTION_COMPLETE] ReportUid:{ReportUid}, Status:{Status}, Total:{Total}, Success:{Success}, Rejected:{Rejected}, WorkingFile:{WorkingFile}",
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
            _logger.LogWarning("[INGESTION_CANCELLED] ReportUid:{ReportUid}", reportUid);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION_ERROR] ReportUid:{ReportUid}", reportUid);

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

    public async Task<IngestionUploadResponse> ResumeAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return CreateFailedResponse(jobId, "JobId is required.");
        }

        var checkpoint = _checkpointRepository.GetByJobId(jobId);
        if (checkpoint == null)
        {
            return CreateFailedResponse(jobId, "No checkpoint found for the provided JobId.");
        }

        if (!checkpoint.IsResumeEligible)
        {
            return new IngestionUploadResponse
            {
                ReportUid = jobId,
                JobId = jobId,
                InboundFileName = checkpoint.InboundFileName,
                Status = checkpoint.Status,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
                Message = "This ingestion job is not eligible for resume.",
                IsResumeEligible = false,
                LastCheckpointUtc = checkpoint.LastCheckpointUtc,
                CheckpointMessage = checkpoint.FailureReason,
                WorkingFileFormat = checkpoint.WorkingFileFormat,
                WorkingFilePath = checkpoint.WorkingFilePath,
                WorkingFileRecordCount = checkpoint.WorkingFileRecordCount
            };
        }

        var response = BuildResumeResponseFromCheckpoint(checkpoint);
        var jobAudit = BuildResumeJobAudit(jobId, checkpoint, response);

        try
        {
            _logger.LogInformation(
                "[INGESTION_RESUME_START] JobId:{JobId}, LastSuccessfulBatch:{LastSuccessfulBatch}, LastProcessedRecordCount:{LastProcessedRecordCount}, TotalBatches:{TotalBatches}",
                jobId,
                checkpoint.LastSuccessfulBatchNumber,
                checkpoint.LastProcessedRecordCount,
                checkpoint.TotalBatches);

            List<FileFinding> recordsToResume;
            try
            {
                recordsToResume = await LoadRecordsForResumeAsync(
                    jobId,
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
                response.CheckpointMessage = "No working or staged records found for resume.";
                UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, response.Message);
                UpdateJobAudit(jobAudit, response, response.Message);
                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(
                    response,
                    cancellationToken: cancellationToken);
                return response;
            }

            if (recordsToResume.Count == 0)
            {
                response.Status = IngestionJobStatus.Success;
                response.CompletedAtUtc = DateTime.UtcNow;
                response.IsResumeEligible = false;
                response.Message = "No remaining records found to resume. Job appears to be already completed.";
                response.CheckpointMessage = "Resume completed. No pending records.";
                UpdateCheckpoint(response, jobAudit, response.Status);
                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(
                    response,
                    cancellationToken: cancellationToken);
                response.MetadataJsonPath = response.ProcessingSummaryPath;
                UpdateJobAudit(jobAudit, response);
                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
                return response;
            }

            await PersistRemainingFindingsInBatchesAsync(
                recordsToResume,
                response,
                jobAudit,
                response.BatchSize,
                checkpoint.LastSuccessfulBatchNumber,
                cancellationToken);

            response.Status = IngestionJobStatus.Success;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.IsResumeEligible = false;
            response.Message = "Ingestion resume completed successfully.";
            response.CheckpointMessage = "Resume completed successfully.";

            UpdateCheckpoint(response, jobAudit, response.Status);
            response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(
                response,
                cancellationToken: cancellationToken);
            response.MetadataJsonPath = response.ProcessingSummaryPath;
            UpdateJobAudit(jobAudit, response);
            await CleanupStagingForCompletedJobAsync(response, cancellationToken);

            _logger.LogInformation(
                "[INGESTION_RESUME_COMPLETE] JobId:{JobId}, Status:{Status}, LastSuccessfulBatch:{LastSuccessfulBatch}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                jobId,
                response.Status,
                response.LastSuccessfulBatchNumber,
                response.LastProcessedRecordCount);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[INGESTION_RESUME_CANCELLED] JobId:{JobId}", jobId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION_RESUME_ERROR] JobId:{JobId}", jobId);

            response.Status = IngestionJobStatus.Failed;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = $"Resume ingestion failed: {ex.Message}";
            response.IsResumeEligible = response.LastProcessedRecordCount < response.SuccessCount;
            response.CheckpointMessage = response.IsResumeEligible
                ? $"Resume can continue from record {response.LastProcessedRecordCount + 1}."
                : "Resume failed.";

            TryUpdateFailureState(response, jobAudit, ex.Message);
            await TryStoreProcessingSummaryAsync(
                response,
                cancellationToken: cancellationToken);
            return response;
        }
    }

    /// <summary>
    /// Ingests a source file already uploaded to storage. This is used by the
    /// Step Function orchestration path.
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
            LogStageDuration(reportUid, "RejectedRowPersistence", rejectedRowsStopwatch, parseResult.RejectedRows.Count);

            var resumeStoreStopwatch = Stopwatch.StartNew();
            var stagingWritten = await PrepareResumeStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings,
                cancellationToken);
            LogStageDuration(reportUid, "ResumeStorePreparation", resumeStoreStopwatch, parseResult.ValidFindings.Count);

            var targetPersistenceStopwatch = Stopwatch.StartNew();
            await PersistValidFindingsInBatchesAsync(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize,
                cancellationToken);
            LogStageDuration(reportUid, "TargetPersistence", targetPersistenceStopwatch, parseResult.ValidFindings.Count);

            CompleteResponse(response);
            UpdateCheckpoint(response, jobAudit, response.Status);

            var summaryStopwatch = Stopwatch.StartNew();
            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows,
                cancellationToken);
            response.MetadataJsonPath = storedMetadataPath;
            response.ProcessingSummaryPath = storedMetadataPath;
            LogStageDuration(reportUid, "ProcessingSummary", summaryStopwatch, parseResult.RejectedRows.Count);

            UpdateJobAudit(jobAudit, response);
            if (stagingWritten)
            {
                var cleanupStopwatch = Stopwatch.StartNew();
                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
                LogStageDuration(reportUid, "StagingCleanup", cleanupStopwatch, parseResult.ValidFindings.Count);
            }
            else
            {
                _logger.LogInformation(
                    "[STAGING_CLEANUP_SKIPPED] JobId:{JobId}, Reason:{Reason}",
                    reportUid,
                    "No staging records were written because verified Parquet is the primary resume store.");
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

    public IngestionUploadResponse? GetStatus(string reportUid)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return null;

        var audit = _jobAuditRepository.GetByJobId(reportUid);
        if (audit == null)
            return null;

        return new IngestionUploadResponse
        {
            ReportUid = audit.ReportUid,
            JobId = audit.JobId,
            InboundFileName = audit.InboundFileName,
            S3FolderPath = audit.S3FolderPath,
            SourceFilePath = audit.SourceFilePath,
            MetadataJsonPath = audit.MetadataJsonPath,
            ArchivedFilePath = audit.ArchivedFilePath,
            ProcessingSummaryPath = audit.ProcessingSummaryPath,
            Status = audit.Status,
            TotalRecords = audit.TotalRecords,
            PayloadRecordCount = audit.PayloadRecordCount,
            SuccessCount = audit.SuccessCount,
            RejectCount = audit.RejectCount,
            ValidationFailureCount = audit.ValidationFailureCount,
            FindingTypeCounts = audit.FindingTypeCounts,
            StartedAtUtc = audit.StartTimestampUtc,
            CompletedAtUtc = audit.EndTimestampUtc,
            BatchSize = audit.BatchSize,
            TotalBatches = audit.TotalBatches,
            PersistedBatchCount = audit.PersistedBatchCount,
            LastSuccessfulBatchNumber = audit.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = audit.LastProcessedRecordCount,
            BatchPersistenceRetryCount = audit.BatchPersistenceRetryCount,
            MaxBatchPersistenceRetryCount = audit.MaxBatchPersistenceRetryCount,
            CheckpointingEnabled = audit.CheckpointingEnabled,
            IsResumeEligible = audit.IsResumeEligible,
            LastCheckpointUtc = audit.LastCheckpointUtc,
            CheckpointMessage = audit.CheckpointMessage,
            WorkingFileFormat = audit.WorkingFileFormat,
            WorkingFilePath = audit.WorkingFilePath,
            WorkingFileRecordCount = audit.WorkingFileRecordCount,
            Message = audit.Status.ToString()
        };
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

    private static IngestionUploadResponse CreateFailedResponse(string jobId, string message)
    {
        return new IngestionUploadResponse
        {
            ReportUid = jobId ?? string.Empty,
            JobId = jobId ?? string.Empty,
            Status = IngestionJobStatus.Failed,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Message = message
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

        // Persist working-file metadata before the first target batch is written.
        // A failure in batch 1 can therefore resume from Parquet.
        UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
    }

    private async Task<bool> PrepareResumeStoreAsync(
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
            // Store the parsed counts before target persistence so a failed first
            // batch can resume from the staging fallback.
            UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
            if (_processingOptions.EnableBatchCheckpointing)
                _jobAuditRepository.Update(jobAudit);
        }
        else if (result.ParquetReady)
        {
            _logger.LogInformation(
                "[STAGING_WRITE_SKIPPED] JobId:{JobId}, Records:{Records}, ResumeStore:{ResumeStore}",
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
                    UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
                    if (ShouldPersistJobAuditProgress(batchNumber, response.TotalBatches))
                        _jobAuditRepository.Update(jobAudit);
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
                UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, ex.Message);
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
            && string.Equals(workingFileFormat, _workingFileStrategy.Format, StringComparison.OrdinalIgnoreCase)
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
                "Resume failed. Neither a readable Parquet working file nor staged records were found for this JobId. Re-upload may be required.");
        }

        return await IngestionAsyncIo.ReadStagedAfterAsync(
            _stagingRepository,
            jobId,
            checkpoint.LastProcessedRecordCount,
            _processingOptions,
            cancellationToken);
    }

    private IngestionJobAudit BuildResumeJobAudit(
        string jobId,
        IngestionCheckpoint checkpoint,
        IngestionUploadResponse response)
    {
        var audit = _jobAuditRepository.GetByJobId(jobId) ?? new IngestionJobAudit
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
        response.ProcessingSummaryPath = audit.ProcessingSummaryPath ?? audit.MetadataJsonPath;
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
        audit.TotalBatches = checkpoint.TotalBatches;
        audit.PersistedBatchCount = checkpoint.PersistedBatchCount;
        audit.LastSuccessfulBatchNumber = checkpoint.LastSuccessfulBatchNumber;
        audit.LastProcessedRecordCount = checkpoint.LastProcessedRecordCount;
        audit.SuccessCount = checkpoint.SuccessCount;
        audit.RejectCount = checkpoint.RejectCount;
        audit.TotalRecords = checkpoint.SuccessCount + checkpoint.RejectCount;
        audit.PayloadRecordCount = audit.TotalRecords;
        audit.ValidationFailureCount = checkpoint.RejectCount;
        audit.BatchPersistenceRetryCount = checkpoint.BatchPersistenceRetryCount;
        audit.MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount;
        audit.CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing;
        audit.Status = IngestionJobStatus.Started;
        audit.IsResumeEligible = checkpoint.IsResumeEligible;
        audit.LastCheckpointUtc = checkpoint.LastCheckpointUtc;
        audit.CheckpointMessage = response.CheckpointMessage;
        audit.WorkingFilePath = response.WorkingFilePath;
        audit.WorkingFileFormat = response.WorkingFileFormat;
        audit.WorkingFileRecordCount = response.WorkingFileRecordCount;
        _jobAuditRepository.Update(audit);

        return audit;
    }

    private IngestionUploadResponse BuildResumeResponseFromCheckpoint(
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
            StartedAtUtc = DateTime.UtcNow,
            Status = IngestionJobStatus.Started,
            BatchSize = checkpoint.BatchSize > 0 ? checkpoint.BatchSize : ResolveBatchSize(),
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
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            IsResumeEligible = checkpoint.IsResumeEligible,
            LastCheckpointUtc = checkpoint.LastCheckpointUtc,
            CheckpointMessage = checkpoint.IsResumeEligible
                ? $"Resume started from batch {checkpoint.LastSuccessfulBatchNumber + 1}."
                : "Job is not eligible for resume.",
            WorkingFilePath = checkpoint.WorkingFilePath,
            WorkingFileFormat = checkpoint.WorkingFileFormat,
            WorkingFileRecordCount = checkpoint.WorkingFileRecordCount
        };
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
            ? $"Ingestion can resume from record {checkpoint.LastProcessedRecordCount + 1}."
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

    private void ValidateUploadedFile(IFormFile? file)
    {
        var extension = file == null ? string.Empty : Path.GetExtension(file.FileName);
        var maxUploadBytes = _processingOptions.MaxUploadFileSizeBytes;

        var errors = new List<string>();
        if (file == null)
            errors.Add("Uploaded file is required.");
        else
        {
            if (file.Length == 0)
                errors.Add("Uploaded file is empty.");
            if (file.Length > maxUploadBytes)
            {
                errors.Add(
                    $"Uploaded file size exceeds the allowed limit of {_processingOptions.MaxUploadFileSizeMb} MB.");
            }
            if (string.IsNullOrWhiteSpace(extension))
                errors.Add("Uploaded file must have a valid file extension.");
            else if (!AllowedUploadExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                errors.Add("Unsupported file format. Only .csv and .xlsx files are allowed.");
        }

        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(" ", errors));
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

    private static string? FirstNotEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record BatchPersistenceRetryState(
        IngestionUploadResponse Response,
        IngestionJobAudit JobAudit,
        int BatchNumber,
        int TotalBatches);
}
