using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class IngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_ProcessesValidCsvAndCompletesAllPersistenceSteps()
    {
        var fixture = new IngestionFixture(ValidCsv());

        var response = await fixture.Service.IngestAsync(
            IngestionFixture.JobId,
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Success, response.Status);
        Assert.Equal(1, response.TotalRecords);
        Assert.Equal(1, response.SuccessCount);
        Assert.Equal(0, response.RejectCount);
        Assert.Equal(1, response.TotalBatches);
        Assert.Equal(1, response.PersistedBatchCount);
        Assert.Equal(1, response.LastProcessedRecordCount);
        Assert.False(response.IsResumeEligible);
        Assert.Single(fixture.PersistedFindings);
        Assert.Equal("source-1.txt", fixture.PersistedFindings[0].FindingFileName);
        Assert.Equal(IngestionJobStatus.Success, fixture.Audit.Status);
        Assert.NotNull(response.ProcessingSummaryPath);
        Assert.Contains(
            fixture.UploadedArtifacts.Keys,
            key => key.EndsWith("report-metadata.json", StringComparison.Ordinal));

        fixture.StagingRepository.Verify(
            repository => repository.SaveValidFindings(
                IngestionFixture.JobId,
                It.Is<List<FileFinding>>(findings => findings.Count == 1)),
            Times.Once);
        fixture.StagingRepository.Verify(
            repository => repository.DeleteByJobId(IngestionFixture.JobId),
            Times.Once);
        fixture.AuditLogger.Verify(
            logger => logger.RecordEvent(
                "IngestionJobCompleted",
                IngestionFixture.JobId,
                "tester",
                IngestionJobStatus.Success.ToString(),
                It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task IngestAsync_ContinuesValidRowsAndReturnsPartialSuccessForInvalidRows()
    {
        var fixture = new IngestionFixture(MixedCsv());

        var response = await fixture.Service.IngestAsync(
            IngestionFixture.JobId,
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.PartialSuccess, response.Status);
        Assert.Equal(2, response.TotalRecords);
        Assert.Equal(1, response.SuccessCount);
        Assert.Equal(1, response.RejectCount);
        Assert.Equal(1, response.ValidationFailureCount);
        Assert.Single(fixture.PersistedFindings);
        Assert.NotEmpty(fixture.PersistedRejectedRows);
        Assert.Contains(
            fixture.PersistedRejectedRows,
            rejected => rejected.FieldName == nameof(FileFinding.FindingType));
        Assert.Equal(IngestionJobStatus.PartialSuccess, fixture.Audit.Status);
    }

    [Fact]
    public async Task IngestAsync_ReturnsFailedWhenEveryRowIsRejected()
    {
        var fixture = new IngestionFixture(InvalidCsv());

        var response = await fixture.Service.IngestAsync(
            IngestionFixture.JobId,
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Failed, response.Status);
        Assert.Equal(1, response.TotalRecords);
        Assert.Equal(0, response.SuccessCount);
        Assert.Equal(1, response.RejectCount);
        Assert.Empty(fixture.PersistedFindings);
        Assert.NotEmpty(fixture.PersistedRejectedRows);
        fixture.FileFindingRepository.Verify(
            repository => repository.AddRange(It.IsAny<IReadOnlyList<FileFinding>>()),
            Times.Never);
        fixture.StagingRepository.Verify(
            repository => repository.SaveValidFindings(
                It.IsAny<string>(),
                It.IsAny<List<FileFinding>>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestAsync_RetriesBatchFailureAndCreatesResumeEligibleCheckpoint()
    {
        var options = CreateOptions();
        options.MaxBatchPersistenceRetryCount = 2;
        options.BatchPersistenceRetryDelayMilliseconds = 0;
        var fixture = new IngestionFixture(ValidCsv(), options);
        fixture.FileFindingRepository
            .Setup(repository => repository.AddRange(It.IsAny<IReadOnlyList<FileFinding>>()))
            .Throws(new IOException("DynamoDB unavailable"));

        var response = await fixture.Service.IngestAsync(
            IngestionFixture.JobId,
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Failed, response.Status);
        Assert.Equal(2, response.BatchPersistenceRetryCount);
        Assert.Equal(0, response.PersistedBatchCount);
        Assert.Equal(0, response.LastProcessedRecordCount);
        Assert.True(response.IsResumeEligible);
        Assert.Contains("Batch persistence failed", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            fixture.Checkpoints,
            checkpoint => checkpoint.Status == IngestionJobStatus.Failed
                && checkpoint.IsResumeEligible
                && checkpoint.LastProcessedRecordCount == 0);
        fixture.FileFindingRepository.Verify(
            repository => repository.AddRange(It.IsAny<IReadOnlyList<FileFinding>>()),
            Times.Exactly(3));
        fixture.StagingRepository.Verify(
            repository => repository.DeleteByJobId(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestAsync_UsesVerifiedParquetAsPrimaryResumeStore()
    {
        var options = CreateOptions();
        options.EnableParquetWorkingFile = true;
        options.UseParquetAsPrimaryResumeStore = true;
        options.LegacyStagingFallbackEnabled = true;
        var fixture = new IngestionFixture(ValidCsv(), options);
        fixture.WorkingFileStrategy
            .Setup(strategy => strategy.WriteAsync(
                IngestionFixture.JobId,
                "report.csv",
                It.IsAny<IReadOnlyList<FileFinding>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionWorkingFileResult
            {
                Format = "parquet",
                Path = "working/report.parquet",
                RecordCount = 1
            });

        var response = await fixture.Service.IngestAsync(
            IngestionFixture.JobId,
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Success, response.Status);
        Assert.Equal("parquet", response.WorkingFileFormat);
        Assert.Equal("working/report.parquet", response.WorkingFilePath);
        Assert.Equal(1, response.WorkingFileRecordCount);
        fixture.StagingRepository.Verify(
            repository => repository.SaveValidFindings(
                It.IsAny<string>(),
                It.IsAny<List<FileFinding>>()),
            Times.Never);
        fixture.StagingRepository.Verify(
            repository => repository.DeleteByJobId(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestAsync_FallsBackToStagingWhenParquetCreationFails()
    {
        var options = CreateOptions();
        options.EnableParquetWorkingFile = true;
        options.UseParquetAsPrimaryResumeStore = true;
        options.LegacyStagingFallbackEnabled = true;
        var fixture = new IngestionFixture(ValidCsv(), options);
        fixture.WorkingFileStrategy
            .Setup(strategy => strategy.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FileFinding>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Parquet storage unavailable"));

        var response = await fixture.Service.IngestAsync(
            IngestionFixture.JobId,
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Success, response.Status);
        Assert.Null(response.WorkingFileFormat);
        Assert.Null(response.WorkingFilePath);
        Assert.Equal(0, response.WorkingFileRecordCount);
        fixture.StagingRepository.Verify(
            repository => repository.SaveValidFindings(
                IngestionFixture.JobId,
                It.Is<List<FileFinding>>(findings => findings.Count == 1)),
            Times.Once);
        fixture.StagingRepository.Verify(
            repository => repository.DeleteByJobId(IngestionFixture.JobId),
            Times.Once);
    }

    [Fact]
    public async Task IngestAsync_ThrowsWhenJobDoesNotExist()
    {
        var fixture = new IngestionFixture(ValidCsv());
        fixture.JobAuditRepository
            .Setup(repository => repository.GetByJobId("missing-job"))
            .Returns((IngestionJobAudit?)null);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Service.IngestAsync("missing-job", CancellationToken.None));

        Assert.Contains("missing-job", exception.Message, StringComparison.Ordinal);
        fixture.Storage.Verify(
            storage => storage.DownloadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestAsync_HonorsCancellationBeforeRepositoryAccess()
    {
        var fixture = new IngestionFixture(ValidCsv());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.IngestAsync(
                IngestionFixture.JobId,
                cancellation.Token));

        fixture.JobAuditRepository.Verify(
            repository => repository.GetByJobId(It.IsAny<string>()),
            Times.Never);
    }

    private static IngestionProcessingOptions CreateOptions()
    {
        return new IngestionProcessingOptions
        {
            BatchSize = 100,
            MinBatchSize = 1,
            MaxBatchSize = 1000,
            EnableBatchCheckpointing = true,
            MaxBatchPersistenceRetryCount = 0,
            BatchPersistenceRetryDelayMilliseconds = 0,
            EnableHighVolumeStreaming = false,
            LegacyFallbackEnabled = true,
            EnableParquetWorkingFile = false,
            UseParquetAsPrimaryResumeStore = false,
            LegacyStagingFallbackEnabled = true,
            RejectedRowBatchSize = 100,
            JobAuditProgressUpdateIntervalBatches = 1
        };
    }

    private static string ValidCsv() =>
        CsvHeader + "\n"
        + @"1,source-1.txt,txt,\\server\share\source-1.txt,Obsolete,SMB,EDG";

    private static string InvalidCsv() =>
        CsvHeader + "\n"
        + @"1,source-1.txt,txt,\\server\share\source-1.txt,Unsupported,SMB,EDG";

    private static string MixedCsv() =>
        CsvHeader + "\n"
        + @"1,source-1.txt,txt,\\server\share\source-1.txt,Obsolete,SMB,EDG" + "\n"
        + @"2,source-2.txt,txt,\\server\share\source-2.txt,Unsupported,SMB,EDG";

    private const string CsvHeader =
        "ID,Finding_File_Name,Finding File Format,Current_File_Location,Finding_Type,Originating_Data_System,Originating_Vendor_Tool";

    private sealed class IngestionFixture
    {
        public const string JobId = "ING-20260721-TEST0001";

        public IngestionFixture(
            string csv,
            IngestionProcessingOptions? options = null)
        {
            Options = options ?? CreateOptions();
            Audit = new IngestionJobAudit
            {
                ReportUid = JobId,
                JobId = JobId,
                InboundFileName = "report.csv",
                FileSizeBytes = Encoding.UTF8.GetByteCount(csv),
                FileFormat = "csv",
                S3FolderPath = "2026/07/ING-20260721-TEST0001/",
                SourceFilePath = "2026/07/ING-20260721-TEST0001/report.csv",
                MetadataJsonPath = "2026/07/ING-20260721-TEST0001/report-metadata.json",
                UploadedBy = "tester",
                UserName = "tester",
                StartedBy = "tester",
                StartTimestampUtc = DateTime.UtcNow.AddMinutes(-1),
                Status = IngestionJobStatus.Started
            };

            FileFindingRepository = new Mock<IFileFindingRepository>();
            Storage = new Mock<IStorageService>();
            JobAuditRepository = new Mock<IIngestionJobAuditRepository>();
            RejectedRowRepository = new Mock<IRejectedRowRepository>();
            CheckpointRepository = new Mock<IIngestionCheckpointRepository>();
            StagingRepository = new Mock<IIngestionStagingRepository>();
            WorkingFileStrategy = new Mock<IIngestionWorkingFileStrategy>();
            AuditLogger = new Mock<IAuditLogger>();

            JobAuditRepository
                .Setup(repository => repository.GetByJobId(JobId))
                .Returns(Audit);
            Storage
                .Setup(storage => storage.DownloadAsync(
                    Audit.SourceFilePath,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)));
            Storage
                .Setup(storage => storage.UploadAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string key, Stream data, CancellationToken _) =>
                {
                    using var copy = new MemoryStream();
                    data.CopyTo(copy);
                    UploadedArtifacts[key] = copy.ToArray();
                    return Task.CompletedTask;
                });
            FileFindingRepository
                .Setup(repository => repository.AddRange(
                    It.IsAny<IReadOnlyList<FileFinding>>()))
                .Callback<IReadOnlyList<FileFinding>>(findings =>
                    PersistedFindings.AddRange(findings));
            RejectedRowRepository
                .Setup(repository => repository.AddRange(
                    It.IsAny<List<RejectedRowDetail>>()))
                .Callback<List<RejectedRowDetail>>(rows =>
                    PersistedRejectedRows.AddRange(rows));
            CheckpointRepository
                .Setup(repository => repository.Upsert(
                    It.IsAny<IngestionCheckpoint>()))
                .Callback<IngestionCheckpoint>(checkpoint =>
                    Checkpoints.Add(CloneCheckpoint(checkpoint)));
            WorkingFileStrategy
                .SetupGet(strategy => strategy.Format)
                .Returns("parquet");

            IValidator<FileFinding> validator = new FileFindingValidator();
            Service = new IngestionService(
                NullLogger<IngestionService>.Instance,
                FileFindingRepository.Object,
                Storage.Object,
                validator,
                JobAuditRepository.Object,
                RejectedRowRepository.Object,
                Microsoft.Extensions.Options.Options.Create(Options),
                CheckpointRepository.Object,
                StagingRepository.Object,
                WorkingFileStrategy.Object,
                AuditLogger.Object);
        }

        public IngestionProcessingOptions Options { get; }

        public IngestionJobAudit Audit { get; }

        public Mock<IFileFindingRepository> FileFindingRepository { get; }

        public Mock<IStorageService> Storage { get; }

        public Mock<IIngestionJobAuditRepository> JobAuditRepository { get; }

        public Mock<IRejectedRowRepository> RejectedRowRepository { get; }

        public Mock<IIngestionCheckpointRepository> CheckpointRepository { get; }

        public Mock<IIngestionStagingRepository> StagingRepository { get; }

        public Mock<IIngestionWorkingFileStrategy> WorkingFileStrategy { get; }

        public Mock<IAuditLogger> AuditLogger { get; }

        public IngestionService Service { get; }

        public List<FileFinding> PersistedFindings { get; } = new();

        public List<RejectedRowDetail> PersistedRejectedRows { get; } = new();

        public List<IngestionCheckpoint> Checkpoints { get; } = new();

        public Dictionary<string, byte[]> UploadedArtifacts { get; } =
            new(StringComparer.Ordinal);

        private static IngestionCheckpoint CloneCheckpoint(IngestionCheckpoint source)
        {
            return new IngestionCheckpoint
            {
                JobId = source.JobId,
                InboundFileName = source.InboundFileName,
                UserName = source.UserName,
                SourceSystem = source.SourceSystem,
                TriggerType = source.TriggerType,
                IngestionMode = source.IngestionMode,
                BatchSize = source.BatchSize,
                TotalBatches = source.TotalBatches,
                LastSuccessfulBatchNumber = source.LastSuccessfulBatchNumber,
                LastProcessedRecordCount = source.LastProcessedRecordCount,
                PersistedBatchCount = source.PersistedBatchCount,
                SuccessCount = source.SuccessCount,
                RejectCount = source.RejectCount,
                BatchPersistenceRetryCount = source.BatchPersistenceRetryCount,
                Status = source.Status,
                IsResumeEligible = source.IsResumeEligible,
                LastCheckpointUtc = source.LastCheckpointUtc,
                FailureReason = source.FailureReason,
                WorkingFilePath = source.WorkingFilePath,
                WorkingFileFormat = source.WorkingFileFormat,
                WorkingFileRecordCount = source.WorkingFileRecordCount
            };
        }
    }
}
