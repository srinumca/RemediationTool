from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one match in {path}, found {count}: {old[:120]!r}")
    file_path.write_text(text.replace(old, new, 1), encoding="utf-8")


def replace_count(path: str, old: str, new: str, expected: int) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"Expected {expected} matches in {path}, found {count}: {old[:120]!r}")
    file_path.write_text(text.replace(old, new), encoding="utf-8")


# -----------------------------------------------------------------------------
# Inbound parser cancellation
# -----------------------------------------------------------------------------
parser_path = "src/RemediationTool.Application/Services/InboundFileParser.cs"
replace_once(
    parser_path,
    '''    public InboundParseResult Parse(
        Stream stream,
        string extension,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return extension.ToLowerInvariant() switch
        {
            ".xlsx" => ParseExcel(stream, jobId, inboundFileName, uploadedBy, loadTime),
            ".csv" => ParseCsv(stream, jobId, inboundFileName, uploadedBy, loadTime),
            _ => throw new InvalidDataException("Unsupported file format. Only .csv and .xlsx files are allowed.")
        };
    }
''',
    '''    public InboundParseResult Parse(
        Stream stream,
        string extension,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        return extension.ToLowerInvariant() switch
        {
            ".xlsx" => ParseExcel(stream, jobId, inboundFileName, uploadedBy, loadTime, cancellationToken),
            ".csv" => ParseCsv(stream, jobId, inboundFileName, uploadedBy, loadTime, cancellationToken),
            _ => throw new InvalidDataException("Unsupported file format. Only .csv and .xlsx files are allowed.")
        };
    }
''')
replace_once(
    parser_path,
    '''    private InboundParseResult ParseExcel(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        using var workbook = new XLWorkbook(stream);
''',
    '''    private InboundParseResult ParseExcel(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook(stream);
''')
replace_once(
    parser_path,
    '''        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            var rowNumber = row.RowNumber();
''',
    '''        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowNumber = row.RowNumber();
''')
replace_once(
    parser_path,
    '''    private InboundParseResult ParseCsv(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime)
    {
        var result = new InboundParseResult();
''',
    '''    private InboundParseResult ParseCsv(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new InboundParseResult();
''')
replace_once(
    parser_path,
    '''        while (TryReadNextCsvRow(csv, jobId, uploadedBy, result, out var rowNumber))
        {
            try
''',
    '''        while (TryReadNextCsvRow(csv, jobId, uploadedBy, result, out var rowNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
''')
replace_once(
    parser_path,
    '''                result.RegisterSourceSystem(finding.OriginatingDataSystem);
                ValidateAndCollect(finding, rowNumber, result);
            }
            catch (Exception ex)
            {
''',
    '''                result.RegisterSourceSystem(finding.OriginatingDataSystem);
                ValidateAndCollect(finding, rowNumber, result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
''')


# -----------------------------------------------------------------------------
# Upload cancellation
# -----------------------------------------------------------------------------
upload_service_path = "src/RemediationTool.Application/Services/UploadService.cs"
replace_once(
    upload_service_path,
    '''    public async Task<UploadResponse> UploadAsync(IFormFile file)
    {
        ValidateFile(file);

        var uploadedAtUtc = DateTime.UtcNow;
''',
    '''    public async Task<UploadResponse> UploadAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);
        cancellationToken.ThrowIfCancellationRequested();

        var uploadedAtUtc = DateTime.UtcNow;
''')
replace_once(
    upload_service_path,
    '''            await _storage.UploadAsync(sourceFilePath, fileStream);
''',
    '''            await _storage.UploadAsync(sourceFilePath, fileStream, cancellationToken);
''')
replace_once(
    upload_service_path,
    '''            sourceFilePath,
            uploadedAtUtc);
''',
    '''            sourceFilePath,
            uploadedAtUtc,
            cancellationToken);
''')
replace_once(
    upload_service_path,
    '''        string s3FolderPath,
        string sourceFilePath,
        DateTime uploadedAtUtc)
''',
    '''        string s3FolderPath,
        string sourceFilePath,
        DateTime uploadedAtUtc,
        CancellationToken cancellationToken)
''')
replace_once(
    upload_service_path,
    '''        await JsonSerializer.SerializeAsync(stream, metadata, MetadataJsonOptions);
        stream.Position = 0;
        await _storage.UploadAsync(metadataPath, stream);
''',
    '''        await JsonSerializer.SerializeAsync(
            stream,
            metadata,
            MetadataJsonOptions,
            cancellationToken);
        stream.Position = 0;
        await _storage.UploadAsync(metadataPath, stream, cancellationToken);
''')

upload_controller_path = "src/RemediationTool.API/Controllers/UploadController.cs"
replace_once(
    upload_controller_path,
    '''    public async Task<IActionResult> Upload(IFormFile file)
''',
    '''    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
''')
replace_once(
    upload_controller_path,
    '''            var response = await _uploadService.UploadAsync(file);
''',
    '''            var response = await _uploadService.UploadAsync(file, cancellationToken);
''')


# -----------------------------------------------------------------------------
# Parquet temporary seekable files and cancellation
# -----------------------------------------------------------------------------
parquet_path = "src/RemediationTool.Infrastructure/Strategies/ParquetIngestionWorkingFileStrategy.cs"
replace_once(
    parquet_path,
    '''using RemediationTool.Domain.Entities;
''',
    '''using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.Storage;
''')
replace_once(
    parquet_path,
    '''        await using var parquetStream = new MemoryStream();
''',
    '''        await using Stream parquetStream = _options.EnableHighVolumeStreaming
            ? TemporarySeekableStream.Create()
            : new MemoryStream();
''')
replace_once(
    parquet_path,
    '''        await _storage.UploadAsync(workingFilePath, parquetStream);

        if (_options.ValidateWorkingFileAfterWrite
            && !await _storage.ExistsAsync(workingFilePath))
''',
    '''        await _storage.UploadAsync(workingFilePath, parquetStream, cancellationToken);

        if (_options.ValidateWorkingFileAfterWrite
            && !await _storage.ExistsAsync(workingFilePath, cancellationToken))
''')
replace_once(
    parquet_path,
    '''        await using var parquetStream = await _storage.DownloadAsync(workingFilePath);
''',
    '''        await using var parquetStream = await OpenSeekableReadAsync(
            workingFilePath,
            cancellationToken);
''')
replace_once(
    parquet_path,
    '''    private static List<ParquetFindingRow> ExtractRows(object? deserializationResult)
''',
    '''    private Task<Stream> OpenSeekableReadAsync(
        string workingFilePath,
        CancellationToken cancellationToken)
    {
        if (_options.EnableHighVolumeStreaming
            && _storage is IStreamingStorageService streamingStorage)
        {
            return streamingStorage.OpenSeekableReadAsync(
                workingFilePath,
                cancellationToken);
        }

        if (_options.EnableHighVolumeStreaming
            && !_options.LegacyFallbackEnabled)
        {
            throw new InvalidOperationException(
                "High-volume Parquet reading requires streaming storage, but no compatible implementation is registered and legacy fallback is disabled.");
        }

        return _storage.DownloadAsync(workingFilePath, cancellationToken);
    }

    private static List<ParquetFindingRow> ExtractRows(object? deserializationResult)
''')


# -----------------------------------------------------------------------------
# Ingestion service asynchronous persistence, streaming and cancellation
# -----------------------------------------------------------------------------
ingestion_path = "src/RemediationTool.Application/Services/IngestionService.cs"
replace_once(
    ingestion_path,
    '''    public async Task<IngestionUploadResponse> ProcessAsync(IFormFile file)
    {
        var startedAtUtc = DateTime.UtcNow;
''',
    '''    public async Task<IngestionUploadResponse> ProcessAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAtUtc = DateTime.UtcNow;
''')
replace_once(
    ingestion_path,
    '''                await _storage.UploadAsync(sourceFilePath, sourceStream);
''',
    '''                await _storage.UploadAsync(sourceFilePath, sourceStream, cancellationToken);
''')
replace_once(
    ingestion_path,
    '''                    inboundFileName,
                    uploadedBy,
                    loadTime);
''',
    '''                    inboundFileName,
                    uploadedBy,
                    loadTime,
                    cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            PersistRejectedRows(reportUid, inboundFileName, parseResult.RejectedRows);
''',
    '''            await PersistRejectedRowsAsync(
                reportUid,
                inboundFileName,
                parseResult.RejectedRows,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            var stagingWritten = await PrepareResumeStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings);
''',
    '''            var stagingWritten = await PrepareResumeStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            PersistValidFindingsInBatches(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize);
''',
    '''            await PersistValidFindingsInBatchesAsync(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows);
''',
    '''            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''                CleanupStagingForCompletedJob(response);
''',
    '''                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION_ERROR] ReportUid:{ReportUid}", reportUid);
''',
    '''            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[INGESTION_CANCELLED] ReportUid:{ReportUid}", reportUid);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION_ERROR] ReportUid:{ReportUid}", reportUid);
''')
replace_once(
    ingestion_path,
    '''            await TryStoreProcessingSummaryAsync(response, parseResult?.RejectedRows);
''',
    '''            await TryStoreProcessingSummaryAsync(
                response,
                parseResult?.RejectedRows,
                cancellationToken);
''')

# Resume method
replace_once(
    ingestion_path,
    '''    public async Task<IngestionUploadResponse> ResumeAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
''',
    '''    public async Task<IngestionUploadResponse> ResumeAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(jobId))
''')
replace_once(
    ingestion_path,
    '''                    checkpoint,
                    response,
                    jobAudit);
''',
    '''                    checkpoint,
                    response,
                    jobAudit,
                    cancellationToken);
''')
replace_count(
    ingestion_path,
    '''                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);
''',
    '''                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(
                    response,
                    cancellationToken: cancellationToken);
''',
    2)
replace_once(
    ingestion_path,
    '''                CleanupStagingForCompletedJob(response);
                return response;
''',
    '''                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
                return response;
''')
replace_once(
    ingestion_path,
    '''            PersistRemainingFindingsInBatches(
                recordsToResume,
                response,
                jobAudit,
                response.BatchSize,
                checkpoint.LastSuccessfulBatchNumber);
''',
    '''            await PersistRemainingFindingsInBatchesAsync(
                recordsToResume,
                response,
                jobAudit,
                response.BatchSize,
                checkpoint.LastSuccessfulBatchNumber,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);
            response.MetadataJsonPath = response.ProcessingSummaryPath;
            UpdateJobAudit(jobAudit, response);
            CleanupStagingForCompletedJob(response);
''',
    '''            response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(
                response,
                cancellationToken: cancellationToken);
            response.MetadataJsonPath = response.ProcessingSummaryPath;
            UpdateJobAudit(jobAudit, response);
            await CleanupStagingForCompletedJobAsync(response, cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION_RESUME_ERROR] JobId:{JobId}", jobId);
''',
    '''            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[INGESTION_RESUME_CANCELLED] JobId:{JobId}", jobId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGESTION_RESUME_ERROR] JobId:{JobId}", jobId);
''')
replace_once(
    ingestion_path,
    '''            await TryStoreProcessingSummaryAsync(response);
''',
    '''            await TryStoreProcessingSummaryAsync(
                response,
                cancellationToken: cancellationToken);
''')

# Step Function ingestion method
replace_once(
    ingestion_path,
    '''    public async Task<IngestionUploadResponse> IngestAsync(string reportUid)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
''',
    '''    public async Task<IngestionUploadResponse> IngestAsync(
        string reportUid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(reportUid))
''')
replace_once(
    ingestion_path,
    '''            await using var fileStream = await _storage.DownloadAsync(jobAudit.SourceFilePath);
''',
    '''            await using var fileStream = await IngestionAsyncIo.OpenSourceReadAsync(
                _storage,
                _processingOptions,
                jobAudit.SourceFilePath,
                extension,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''                jobAudit.InboundFileName,
                uploadedBy,
                loadTime);
''',
    '''                jobAudit.InboundFileName,
                uploadedBy,
                loadTime,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            PersistRejectedRows(reportUid, jobAudit.InboundFileName, parseResult.RejectedRows);
''',
    '''            await PersistRejectedRowsAsync(
                reportUid,
                jobAudit.InboundFileName,
                parseResult.RejectedRows,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            var stagingWritten = await PrepareResumeStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings);
''',
    '''            var stagingWritten = await PrepareResumeStoreAsync(
                response,
                jobAudit,
                parseResult.ValidFindings,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            PersistValidFindingsInBatches(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize);
''',
    '''            await PersistValidFindingsInBatchesAsync(
                parseResult.ValidFindings,
                response,
                jobAudit,
                configuredBatchSize,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows);
''',
    '''            var storedMetadataPath = await StoreProcessingSummaryAsync(
                response,
                parseResult.RejectedRows,
                cancellationToken);
''')
replace_once(
    ingestion_path,
    '''                CleanupStagingForCompletedJob(response);
''',
    '''                await CleanupStagingForCompletedJobAsync(response, cancellationToken);
''')
replace_once(
    ingestion_path,
    '''            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGEST_ASYNC_ERROR] ReportUid:{ReportUid}", reportUid);
''',
    '''            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[INGEST_ASYNC_CANCELLED] ReportUid:{ReportUid}", reportUid);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INGEST_ASYNC_ERROR] ReportUid:{ReportUid}", reportUid);
''')
replace_once(
    ingestion_path,
    '''            await TryStoreProcessingSummaryAsync(response, parseResult?.RejectedRows);
''',
    '''            await TryStoreProcessingSummaryAsync(
                response,
                parseResult?.RejectedRows,
                cancellationToken);
''')

# Working file + staging preparation
replace_once(
    ingestion_path,
    '''    private async Task CreateWorkingFileAsync(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IReadOnlyList<FileFinding> validFindings)
''',
    '''    private async Task CreateWorkingFileAsync(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken)
''')
replace_once(
    ingestion_path,
    '''            response.InboundFileName,
            validFindings);
''',
    '''            response.InboundFileName,
            validFindings,
            cancellationToken);
''')
replace_once(
    ingestion_path,
    '''    private async Task<bool> PrepareResumeStoreAsync(
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        IReadOnlyList<FileFinding> validFindings)
    {
        var result = await IngestionResumeStoreCoordinator.PrepareAsync(
            _processingOptions,
            validFindings.Count,
            createParquetAsync: () => CreateWorkingFileAsync(response, jobAudit, validFindings),
            writeStaging: () => _stagingRepository.SaveValidFindings(response.JobId, validFindings.ToList()),
            clearParquetMetadata: () => ClearWorkingFileMetadata(response, jobAudit));
''',
    '''    private async Task<bool> PrepareResumeStoreAsync(
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
''')

# Finding persistence methods
replace_once(
    ingestion_path,
    '''    private void PersistValidFindingsInBatches(
        List<FileFinding> validFindings,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        int batchSize)
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
            batchNumber++;
            IReadOnlyList<FileFinding> records = chunk;

            try
            {
                PersistBatchWithRetry(
                    records,
                    batchNumber,
                    response.TotalBatches,
                    response,
                    jobAudit);

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
''',
    '''    private async Task PersistValidFindingsInBatchesAsync(
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
''')
replace_once(
    ingestion_path,
    '''    private void PersistRemainingFindingsInBatches(
        List<FileFinding> remainingFindings,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        int batchSize,
        int lastSuccessfulBatchNumber)
    {
        if (remainingFindings.Count == 0)
            return;

        var batchNumber = lastSuccessfulBatchNumber;
        foreach (var chunk in remainingFindings.Chunk(batchSize))
        {
            batchNumber++;
            IReadOnlyList<FileFinding> records = chunk;

            try
            {
                PersistBatchWithRetry(
                    records,
                    batchNumber,
                    response.TotalBatches,
                    response,
                    jobAudit);

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
''',
    '''    private async Task PersistRemainingFindingsInBatchesAsync(
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
''')
replace_once(
    ingestion_path,
    '''    private void PersistBatchWithRetry(
        IReadOnlyList<FileFinding> records,
        int batchNumber,
        int totalBatches,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
    {
        var previousState = _batchPersistenceRetryState.Value;
        _batchPersistenceRetryState.Value = new BatchPersistenceRetryState(
            response,
            jobAudit,
            batchNumber,
            totalBatches);

        try
        {
            _batchPersistencePipeline.Execute(() => _repository.AddRange(records));
        }
        finally
        {
            _batchPersistenceRetryState.Value = previousState;
        }
    }
''',
    '''    private async Task PersistBatchWithRetryAsync(
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
''')

# Resume loading
replace_once(
    ingestion_path,
    '''    private async Task<List<FileFinding>> LoadRecordsForResumeAsync(
        string jobId,
        IngestionCheckpoint checkpoint,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit)
''',
    '''    private async Task<List<FileFinding>> LoadRecordsForResumeAsync(
        string jobId,
        IngestionCheckpoint checkpoint,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        CancellationToken cancellationToken)
''')
replace_once(
    ingestion_path,
    '''                    workingFilePath,
                    checkpoint.LastProcessedRecordCount);
''',
    '''                    workingFilePath,
                    checkpoint.LastProcessedRecordCount,
                    cancellationToken);
''')
replace_once(
    ingestion_path,
    '''        var stagedCount = _stagingRepository.CountByJobId(jobId);
''',
    '''        var stagedCount = await IngestionAsyncIo.CountStagedAsync(
            _stagingRepository,
            jobId,
            _processingOptions,
            cancellationToken);
''')
replace_once(
    ingestion_path,
    '''        return _stagingRepository.GetValidFindingsAfter(
            jobId,
            checkpoint.LastProcessedRecordCount);
''',
    '''        return await IngestionAsyncIo.ReadStagedAfterAsync(
            _stagingRepository,
            jobId,
            checkpoint.LastProcessedRecordCount,
            _processingOptions,
            cancellationToken);
''')

# Summary + rejected rows
replace_once(
    ingestion_path,
    '''    private async Task<string> StoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        IReadOnlyCollection<RejectedRowSummary>? rejectedRows = null)
''',
    '''    private async Task<string> StoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        IReadOnlyCollection<RejectedRowSummary>? rejectedRows = null,
        CancellationToken cancellationToken = default)
''')
replace_once(
    ingestion_path,
    '''        await _storage.UploadAsync(summaryKey, stream);
''',
    '''        await _storage.UploadAsync(summaryKey, stream, cancellationToken);
''')
replace_once(
    ingestion_path,
    '''    private void PersistRejectedRows(
        string jobId,
        string inboundFileName,
        IReadOnlyCollection<RejectedRowSummary> rejectedRows)
    {
        if (rejectedRows.Count == 0)
            return;

        var details = new List<RejectedRowDetail>(rejectedRows.Count);
        foreach (var row in rejectedRows)
        {
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
        }

        _rejectedRowRepository.AddRange(details);
    }
''',
    '''    private async Task PersistRejectedRowsAsync(
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
''')

# Failure summary + cleanup
replace_once(
    ingestion_path,
    '''    private async Task TryStoreProcessingSummaryAsync(
        IngestionUploadResponse response,
        IReadOnlyCollection<RejectedRowSummary>? rejectedRows = null)
    {
        try
        {
            var path = await StoreProcessingSummaryAsync(response, rejectedRows);
''',
    '''    private async Task TryStoreProcessingSummaryAsync(
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
''')
replace_once(
    ingestion_path,
    '''        catch (Exception summaryEx)
        {
''',
    '''        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception summaryEx)
        {
''')
replace_once(
    ingestion_path,
    '''    private void CleanupStagingForCompletedJob(IngestionUploadResponse response)
    {
        if (response.Status != IngestionJobStatus.Success
            && response.Status != IngestionJobStatus.PartialSuccess)
        {
            return;
        }

        try
        {
            _stagingRepository.DeleteByJobId(response.JobId);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogWarning(
                cleanupEx,
                "[STAGING_CLEANUP_FAILED] JobId:{JobId}",
                response.JobId);
        }
    }
''',
    '''    private async Task CleanupStagingForCompletedJobAsync(
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
''')

print("Phase 2 ingestion transformations applied successfully.")
