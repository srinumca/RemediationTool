using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RemediationTool.Application.Exceptions;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class IngestionResumeServiceTests
{
    [Fact]
    public async Task ResumeAsync_WhenCheckpointDoesNotExist_ThrowsKeyNotFoundException()
    {
        var dependencies = CreateDependencies();
        dependencies.Checkpoints
            .Setup(repository => repository.GetByJobId("missing-job"))
            .Returns((IngestionCheckpoint?)null);

        var service = CreateService(dependencies);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ResumeAsync("missing-job", CancellationToken.None));

        Assert.Contains("No checkpoint found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResumeAsync_WhenCheckpointIsNotEligible_ReturnsWithoutReprocessing()
    {
        var dependencies = CreateDependencies();
        dependencies.Checkpoints
            .Setup(repository => repository.GetByJobId("completed-job"))
            .Returns(new IngestionCheckpoint
            {
                JobId = "completed-job",
                InboundFileName = "report.csv",
                Status = IngestionJobStatus.Success,
                IsResumeEligible = false,
                SuccessCount = 10,
                LastProcessedRecordCount = 10,
                LastSuccessfulBatchNumber = 1,
                PersistedBatchCount = 1,
                TotalBatches = 1,
                BatchSize = 100,
                LastCheckpointUtc = DateTime.UtcNow
            });

        var service = CreateService(dependencies);

        var response = await service.ResumeAsync(
            "completed-job",
            CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Success, response.Status);
        Assert.False(response.IsResumeEligible);
        Assert.Contains("not eligible", response.Message, StringComparison.OrdinalIgnoreCase);
        dependencies.Findings.Verify(
            repository => repository.AddRange(It.IsAny<IReadOnlyList<FileFinding>>()),
            Times.Never);
    }

    [Fact]
    public async Task ResumeAsync_WhenRecoveryDataIsUnavailable_ThrowsUnprocessableResumeException()
    {
        var dependencies = CreateDependencies();
        var checkpoint = new IngestionCheckpoint
        {
            JobId = "failed-job",
            InboundFileName = "report.csv",
            UserName = "tester",
            Status = IngestionJobStatus.Failed,
            IsResumeEligible = true,
            SuccessCount = 10,
            RejectCount = 0,
            LastProcessedRecordCount = 0,
            LastSuccessfulBatchNumber = 0,
            PersistedBatchCount = 0,
            TotalBatches = 1,
            BatchSize = 100,
            LastCheckpointUtc = DateTime.UtcNow
        };

        dependencies.Checkpoints
            .Setup(repository => repository.GetByJobId("failed-job"))
            .Returns(checkpoint);
        dependencies.JobAudits
            .Setup(repository => repository.GetByJobId("failed-job"))
            .Returns(new IngestionJobAudit
            {
                ReportUid = "failed-job",
                JobId = "failed-job",
                InboundFileName = "report.csv",
                UserName = "tester",
                UploadedBy = "tester"
            });
        dependencies.Staging
            .Setup(repository => repository.CountByJobId("failed-job"))
            .Returns(0);
        dependencies.Storage
            .Setup(storage => storage.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(dependencies);

        var exception = await Assert.ThrowsAsync<IngestionResumeDataUnavailableException>(() =>
            service.ResumeAsync("failed-job", CancellationToken.None));

        Assert.Equal(IngestionJobStatus.Failed, exception.Response.Status);
        Assert.False(exception.Response.IsResumeEligible);
        Assert.Contains("Parquet", exception.Response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResumeAsync_WhenCancellationIsAlreadyRequested_StopsBeforeRepositoryReads()
    {
        var dependencies = CreateDependencies();
        var service = CreateService(dependencies);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ResumeAsync("cancelled-job", cancellation.Token));

        dependencies.Checkpoints.Verify(
            repository => repository.GetByJobId(It.IsAny<string>()),
            Times.Never);
    }

    private static IngestionResumeService CreateService(Dependencies dependencies)
    {
        return new IngestionResumeService(
            NullLogger<IngestionResumeService>.Instance,
            dependencies.Findings.Object,
            dependencies.Storage.Object,
            dependencies.JobAudits.Object,
            Microsoft.Extensions.Options.Options.Create(new IngestionProcessingOptions
            {
                BatchSize = 100,
                MinBatchSize = 100,
                MaxBatchSize = 1000,
                EnableBatchCheckpointing = true,
                EnableParquetWorkingFile = true,
                LegacyFallbackEnabled = true,
                MaxBatchPersistenceRetryCount = 1,
                BatchPersistenceRetryDelayMilliseconds = 0
            }),
            dependencies.Checkpoints.Object,
            dependencies.Staging.Object,
            dependencies.WorkingFile.Object,
            dependencies.AuditLogger.Object);
    }

    private static Dependencies CreateDependencies()
    {
        var workingFile = new Mock<IIngestionWorkingFileStrategy>();
        workingFile.SetupGet(strategy => strategy.Format).Returns("Parquet");

        return new Dependencies(
            new Mock<IFileFindingRepository>(),
            new Mock<IStorageService>(),
            new Mock<IIngestionJobAuditRepository>(),
            new Mock<IIngestionCheckpointRepository>(),
            new Mock<IIngestionStagingRepository>(),
            workingFile,
            new Mock<IAuditLogger>());
    }

    private sealed record Dependencies(
        Mock<IFileFindingRepository> Findings,
        Mock<IStorageService> Storage,
        Mock<IIngestionJobAuditRepository> JobAudits,
        Mock<IIngestionCheckpointRepository> Checkpoints,
        Mock<IIngestionStagingRepository> Staging,
        Mock<IIngestionWorkingFileStrategy> WorkingFile,
        Mock<IAuditLogger> AuditLogger);
}
