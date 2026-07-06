using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Quarantine service — queues eligible obsolete records and moves pending records
/// through the quarantine lifecycle with retry, audit, and error categorisation.
/// </summary>
public class QuarantineService
{
    private readonly IFileFindingRepository _repository;
    private readonly IQuarantineFileService _fileService;
    private readonly ILogger<QuarantineService> _logger;
    private readonly IAuditLogger _auditLogger;
    private readonly QuarantineProcessingOptions _options;

    public QuarantineService(
        IFileFindingRepository repository,
        IQuarantineFileService fileService,
        ILogger<QuarantineService> logger,
        IAuditLogger auditLogger,
        IOptions<QuarantineProcessingOptions> options)
    {
        _repository = repository;
        _fileService = fileService;
        _logger = logger;
        _auditLogger = auditLogger;
        _options = options.Value;
    }

    /// <summary>
    /// Queues selected NotYetStarted obsolete records for quarantine and optionally processes them immediately.
    /// </summary>
    public async Task<QuarantineBatchResult> QueueAsync(
        QuarantineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = CreateResult();
        var requestedBy = ResolveActor(request.RequestedBy);
        var requestedIds = request.RecordIds?.Distinct().ToList() ?? new List<Guid>();

        _logger.LogInformation(
            "[QUARANTINE_QUEUE_START] RunId:{RunId}, RequestedBy:{RequestedBy}, IncludeAllEligible:{IncludeAllEligible}, RequestedIds:{RequestedIdsCount}, ProcessImmediately:{ProcessImmediately}",
            result.RunId, requestedBy, request.IncludeAllEligibleNotYetStarted, requestedIds.Count, request.ProcessImmediately);

        var candidateRecords = GetQueueCandidates(request.IncludeAllEligibleNotYetStarted, requestedIds);
        result.RequestedCount = request.IncludeAllEligibleNotYetStarted ? candidateRecords.Count : requestedIds.Count;

        if (!request.IncludeAllEligibleNotYetStarted)
        {
            AddMissingRecordResults(result, requestedIds, candidateRecords.Select(x => x.Id));
        }

        var queuedIds = new List<Guid>();

        foreach (var file in candidateRecords)
        {
            if (!IsEligibleForQuarantineQueue(file))
            {
                result.Items.Add(BuildSkippedResult(
                    file,
                    $"Record is not eligible for quarantine queue. FindingType:{file.FindingType}, Status:{file.Status}."));
                continue;
            }

            var previousStatus = file.Status;
            file.Status = FileStatus.PendingQuarantine;
            file.ErrorReason = string.Empty;
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            queuedIds.Add(file.Id);
            result.QueuedCount++;

            _logger.LogInformation(
                "[QUARANTINE_QUEUED] RunId:{RunId}, RecordId:{RecordId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, PreviousStatus:{PreviousStatus}, NewStatus:{NewStatus}, RequestedBy:{RequestedBy}",
                result.RunId, file.Id, file.SourceRecordId, file.FileName, previousStatus, file.Status, requestedBy);

            _auditLogger.RecordEvent(
                eventType: "FileQueuedForQuarantine",
                entityId: file.Id.ToString(),
                actor: requestedBy,
                outcome: "Success",
                details: new { file.FileName, file.SourceRecordId, PreviousStatus = previousStatus.ToString(), NewStatus = file.Status.ToString() });

            if (!request.ProcessImmediately)
            {
                result.Items.Add(new QuarantineItemResult
                {
                    RecordId = file.Id,
                    SourceRecordId = file.SourceRecordId,
                    FileName = file.FileName,
                    StartingStatus = previousStatus,
                    FinalStatus = file.Status,
                    Succeeded = true,
                    Message = "Record queued for quarantine.",
                    ErrorCategory = ErrorCategory.None.ToString()
                });
            }
        }

        if (request.ProcessImmediately && queuedIds.Count > 0)
        {
            var processResult = await ProcessPendingAsync(queuedIds, requestedBy, result.RunId, cancellationToken);
            result.Items.AddRange(processResult.Items);
            result.ProcessedCount = processResult.ProcessedCount;
            result.SucceededCount = processResult.SucceededCount;
            result.FailedCount = processResult.FailedCount;
            result.SkippedCount += processResult.SkippedCount;
        }

        FinaliseResult(result, request.ProcessImmediately ? "Quarantine queue and processing completed." : "Quarantine queue completed.");

        _logger.LogInformation(
            "[QUARANTINE_QUEUE_COMPLETE] RunId:{RunId}, Requested:{Requested}, Queued:{Queued}, Processed:{Processed}, Succeeded:{Succeeded}, Failed:{Failed}, Skipped:{Skipped}",
            result.RunId, result.RequestedCount, result.QueuedCount, result.ProcessedCount, result.SucceededCount, result.FailedCount, result.SkippedCount);

        return result;
    }

    /// <summary>
    /// Processes all records currently in PendingQuarantine.
    /// </summary>
    public Task<QuarantineBatchResult> ProcessAsync(CancellationToken cancellationToken = default)
        => ProcessPendingAsync(recordIds: null, requestedBy: "System", runId: null, cancellationToken);

    private async Task<QuarantineBatchResult> ProcessPendingAsync(
        IReadOnlyCollection<Guid>? recordIds,
        string requestedBy,
        string? runId,
        CancellationToken cancellationToken)
    {
        var result = CreateResult(runId);
        requestedBy = ResolveActor(requestedBy);

        _logger.LogInformation(
            "[QUARANTINE_RUN_START] RunId:{RunId}, RequestedBy:{RequestedBy}, FilteredRecordCount:{FilteredRecordCount}, RetentionYears:{RetentionYears}, MaxRetryAttempts:{MaxRetryAttempts}",
            result.RunId, requestedBy, recordIds?.Count ?? 0, _options.RetentionYears, _options.MaxRetryAttempts);

        var files = GetPendingQuarantineRecords(recordIds);

        result.RequestedCount = recordIds?.Count ?? files.Count;
        result.ProcessedCount = files.Count;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemResult = await ProcessSingleAsync(file, requestedBy, result.RunId, cancellationToken);
            result.Items.Add(itemResult);

            if (itemResult.Succeeded) result.SucceededCount++;
            else if (itemResult.Skipped) result.SkippedCount++;
            else result.FailedCount++;
        }

        FinaliseResult(result, "Quarantine processing completed.");

        _logger.LogInformation(
            "[QUARANTINE_RUN_COMPLETE] RunId:{RunId}, Processed:{Processed}, Succeeded:{Succeeded}, Failed:{Failed}, Skipped:{Skipped}",
            result.RunId, result.ProcessedCount, result.SucceededCount, result.FailedCount, result.SkippedCount);

        return result;
    }

    private async Task<QuarantineItemResult> ProcessSingleAsync(
        FileFinding file,
        string requestedBy,
        string runId,
        CancellationToken cancellationToken)
    {
        var startingStatus = file.Status;
        var sourcePath = _fileService.ResolveSourcePath(file);
        var quarantinePath = string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : _fileService.BuildQuarantinePath(file, sourcePath);
        var stubPath = string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : _fileService.BuildStubPath(sourcePath);

        _logger.LogInformation(
            "[QUARANTINE_FILE_START] RunId:{RunId}, RecordId:{RecordId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, Status:{Status}, SourcePath:{SourcePath}, QuarantinePath:{QuarantinePath}",
            runId, file.Id, file.SourceRecordId, file.FileName, file.Status, sourcePath, quarantinePath);

        if (file.Status != FileStatus.PendingQuarantine)
        {
            return BuildSkippedResult(file, $"Record is not pending quarantine. Current Status:{file.Status}.", startingStatus);
        }

        if (!IsRetentionEligible(file))
        {
            file.Status = FileStatus.NotYetStarted;
            file.ErrorReason = $"Retention threshold not reached. LastModifiedDate:{file.LastModifiedDate:O}, RetentionYears:{_options.RetentionYears}.";
            file.ErrorCategory = ErrorCategory.None.ToString();
            file.UpdatedDate = DateTime.UtcNow;
            _repository.Update(file);

            _logger.LogInformation(
                "[QUARANTINE_FILE_SKIPPED_RETENTION] RunId:{RunId}, RecordId:{RecordId}, FileName:{FileName}, LastModifiedDate:{LastModifiedDate:O}, RetentionYears:{RetentionYears}",
                runId, file.Id, file.FileName, file.LastModifiedDate, _options.RetentionYears);

            return BuildSkippedResult(file, file.ErrorReason, startingStatus, sourcePath, quarantinePath);
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return MarkFailed(
                file,
                startingStatus,
                "Source path is missing.",
                ErrorCategory.MissingAtSource,
                requestedBy,
                runId,
                sourcePath,
                quarantinePath,
                attemptCount: 0);
        }

        if (!await _fileService.ExistsAsync(sourcePath, cancellationToken))
        {
            return MarkFailed(
                file,
                startingStatus,
                $"File not found at source: {sourcePath}",
                ErrorCategoryResolver.SourceFileMissing(),
                requestedBy,
                runId,
                sourcePath,
                quarantinePath,
                attemptCount: 0);
        }

        file.Status = FileStatus.InProgress;
        file.ErrorReason = string.Empty;
        file.ErrorCategory = ErrorCategory.None.ToString();
        file.UpdatedDate = DateTime.UtcNow;
        _repository.Update(file);

        Exception? lastException = null;
        var lastCategory = ErrorCategory.Others;
        var maxAttempts = Math.Max(1, _options.MaxRetryAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "[QUARANTINE_ATTEMPT_START] RunId:{RunId}, RecordId:{RecordId}, Attempt:{Attempt}/{MaxAttempts}, SourcePath:{SourcePath}, QuarantinePath:{QuarantinePath}",
                    runId, file.Id, attempt, maxAttempts, sourcePath, quarantinePath);

                await _fileService.CopyAsync(sourcePath, quarantinePath, cancellationToken);
                await _fileService.WriteStubAsync(stubPath, _options.StubMessage, cancellationToken);
                await _fileService.DeleteSourceAsync(sourcePath, cancellationToken);

                file.OriginalFileLocation ??= sourcePath;
                file.Status = FileStatus.QuarantineComplete;
                file.QuarantinePath = quarantinePath;
                file.QuarantineDate = DateTime.UtcNow;
                file.ErrorReason = string.Empty;
                file.ErrorCategory = ErrorCategory.None.ToString();
                file.UpdatedDate = DateTime.UtcNow;
                _repository.Update(file);

                _logger.LogInformation(
                    "[QUARANTINE_FILE_COMPLETE] RunId:{RunId}, RecordId:{RecordId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, Attempt:{Attempt}, QuarantinePath:{QuarantinePath}, StubPath:{StubPath}",
                    runId, file.Id, file.SourceRecordId, file.FileName, attempt, quarantinePath, stubPath);

                _auditLogger.RecordEvent(
                    eventType: "FileQuarantined",
                    entityId: file.Id.ToString(),
                    actor: requestedBy,
                    outcome: "Success",
                    details: new
                    {
                        file.FileName,
                        file.SourceRecordId,
                        OriginalPath = sourcePath,
                        QuarantinePath = quarantinePath,
                        StubPath = stubPath,
                        AttemptCount = attempt
                    });

                return new QuarantineItemResult
                {
                    RecordId = file.Id,
                    SourceRecordId = file.SourceRecordId,
                    FileName = file.FileName,
                    StartingStatus = startingStatus,
                    FinalStatus = file.Status,
                    Succeeded = true,
                    AttemptCount = attempt,
                    Message = "File quarantined successfully.",
                    ErrorCategory = ErrorCategory.None.ToString(),
                    OriginalPath = sourcePath,
                    QuarantinePath = quarantinePath
                };
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                lastCategory = ErrorCategoryResolver.FromException(ex);

                _logger.LogWarning(ex,
                    "[QUARANTINE_ATTEMPT_FAILED] RunId:{RunId}, RecordId:{RecordId}, FileName:{FileName}, Attempt:{Attempt}/{MaxAttempts}, ErrorCategory:{ErrorCategory}, NextRetryDelayMs:{RetryDelayMs}",
                    runId, file.Id, file.FileName, attempt, maxAttempts, lastCategory, _options.RetryDelayMilliseconds);

                await Task.Delay(Math.Max(0, _options.RetryDelayMilliseconds), cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                lastCategory = ErrorCategoryResolver.FromException(ex);
            }
        }

        var errorReason = lastException == null
            ? "Quarantine failed after retry attempts."
            : $"Quarantine failed after {maxAttempts} attempt(s). Last error: {lastException.Message}";

        return MarkFailed(
            file,
            startingStatus,
            errorReason,
            ErrorCategory.RetryExhausted,
            requestedBy,
            runId,
            sourcePath,
            quarantinePath,
            maxAttempts,
            lastCategory.ToString());
    }

    private QuarantineItemResult MarkFailed(
        FileFinding file,
        FileStatus startingStatus,
        string errorReason,
        ErrorCategory errorCategory,
        string requestedBy,
        string runId,
        string? sourcePath,
        string? quarantinePath,
        int attemptCount,
        string? lastResolvedCategory = null)
    {
        file.Status = FileStatus.Error;
        file.ErrorReason = errorReason;
        file.ErrorCategory = errorCategory.ToString();
        file.UpdatedDate = DateTime.UtcNow;
        _repository.Update(file);

        _logger.LogError(
            "[QUARANTINE_FILE_FAILED] RunId:{RunId}, RecordId:{RecordId}, SourceRecordId:{SourceRecordId}, FileName:{FileName}, ErrorCategory:{ErrorCategory}, LastResolvedCategory:{LastResolvedCategory}, AttemptCount:{AttemptCount}, ErrorReason:{ErrorReason}",
            runId, file.Id, file.SourceRecordId, file.FileName, errorCategory, lastResolvedCategory, attemptCount, errorReason);

        _auditLogger.RecordEvent(
            eventType: "FileQuarantined",
            entityId: file.Id.ToString(),
            actor: requestedBy,
            outcome: "Failed",
            details: new
            {
                file.FileName,
                file.SourceRecordId,
                Error = errorReason,
                ErrorCategory = errorCategory.ToString(),
                LastResolvedCategory = lastResolvedCategory,
                AttemptCount = attemptCount,
                OriginalPath = sourcePath,
                QuarantinePath = quarantinePath
            });

        return new QuarantineItemResult
        {
            RecordId = file.Id,
            SourceRecordId = file.SourceRecordId,
            FileName = file.FileName,
            StartingStatus = startingStatus,
            FinalStatus = file.Status,
            Succeeded = false,
            Skipped = false,
            AttemptCount = attemptCount,
            Message = errorReason,
            ErrorCategory = errorCategory.ToString(),
            OriginalPath = sourcePath,
            QuarantinePath = quarantinePath
        };
    }

    private IReadOnlyList<FileFinding> GetQueueCandidates(bool includeAllEligible, IReadOnlyCollection<Guid> requestedIds)
    {
        if (includeAllEligible)
        {
            var obsoleteRecords = _repository.GetLatestByFindingType(FindingType.Obsolete);
            return obsoleteRecords.Where(IsEligibleForQuarantineQueue).ToList();
        }

        if (requestedIds.Count == 0)
            return Array.Empty<FileFinding>();

        return requestedIds
            .Distinct()
            .Select(id => _repository.GetById(id))
            .Where(file => file != null)
            .Cast<FileFinding>()
            .ToList();
    }

    private IReadOnlyList<FileFinding> GetPendingQuarantineRecords(IReadOnlyCollection<Guid>? recordIds)
    {
        if (recordIds is { Count: > 0 })
        {
            return recordIds
                .Distinct()
                .Select(id => _repository.GetById(id))
                .Where(file => file != null && file.Status == FileStatus.PendingQuarantine)
                .Cast<FileFinding>()
                .ToList();
        }

        return _repository.GetAll()
            .Where(x => x.Status == FileStatus.PendingQuarantine)
            .ToList();
    }

    private static bool IsEligibleForQuarantineQueue(FileFinding file)
        => file.Status == FileStatus.NotYetStarted
           && string.Equals(file.FindingType, FindingType.Obsolete, StringComparison.OrdinalIgnoreCase);

    private bool IsRetentionEligible(FileFinding file)
    {
        if (!_options.EnableRetentionCheck)
            return true;

        if (!file.LastModifiedDateUtc.HasValue)
            return false;

        return file.LastModifiedDateUtc.Value <= DateTime.UtcNow.AddYears(-Math.Max(0, _options.RetentionYears));
    }

    private static QuarantineBatchResult CreateResult(string? runId = null)
        => new()
        {
            RunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId,
            StartedAtUtc = DateTime.UtcNow
        };

    private static void FinaliseResult(QuarantineBatchResult result, string message)
    {
        result.CompletedAtUtc = DateTime.UtcNow;
        result.SkippedCount = result.Items.Count(x => x.Skipped);
        result.Message = message;
    }

    private static string ResolveActor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim();

    private static QuarantineItemResult BuildSkippedResult(
        FileFinding file,
        string message,
        FileStatus? startingStatus = null,
        string? sourcePath = null,
        string? quarantinePath = null)
        => new()
        {
            RecordId = file.Id,
            SourceRecordId = file.SourceRecordId,
            FileName = file.FileName,
            StartingStatus = startingStatus ?? file.Status,
            FinalStatus = file.Status,
            Succeeded = false,
            Skipped = true,
            Message = message,
            ErrorCategory = file.ErrorCategory,
            OriginalPath = sourcePath,
            QuarantinePath = quarantinePath
        };

    private static void AddMissingRecordResults(
        QuarantineBatchResult result,
        IReadOnlyCollection<Guid> requestedIds,
        IEnumerable<Guid> foundIds)
    {
        var foundSet = foundIds.ToHashSet();
        foreach (var missingId in requestedIds.Where(id => !foundSet.Contains(id)))
        {
            result.Items.Add(new QuarantineItemResult
            {
                RecordId = missingId,
                StartingStatus = FileStatus.NotYetStarted,
                FinalStatus = FileStatus.NotYetStarted,
                Skipped = true,
                Message = "Record was not found."
            });
        }
    }
}
