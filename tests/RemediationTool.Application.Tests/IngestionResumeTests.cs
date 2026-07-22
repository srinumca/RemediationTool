using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class IngestionResumeTests
{
    [Fact]
    public async Task ResumeAsync_ReturnsFailedResponseWhenCheckpointDoesNotExist()
    {
        var checkpointRepository = new Mock<IIngestionCheckpointRepository>();
        checkpointRepository
            .Setup(repository => repository.GetByJobId("missing-job"))
            .Returns((IngestionCheckpoint?)null);
        var service = CreateService(checkpointRepository);

        var response = await service.ResumeAsync("missing-job", CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Failed, response.Status);
        Assert.False(response.IsResumeEligible);
        Assert.Contains("No checkpoint found", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResumeAsync_DoesNotReprocessJobThatIsNotResumeEligible()
    {
        var checkpointRepository = new Mock<IIngestionCheckpointRepository>();
        checkpointRepository
            .Setup(repository => repository.GetByJobId("completed-job"))
            .Returns(new IngestionCheckpoint
            {
                JobId = "completed-job",
                InboundFileName = "report.csv",
                Status = IngestionJobStatus.Success,
                IsResumeEligible = false,
                LastProcessedRecordCount = 10,
                SuccessCount = 10,
                LastCheckpointUtc = DateTime.UtcNow
            });
        var stagingRepository = new Mock<IIngestionStagingRepository>();
        var workingFileStrategy = new Mock<IIngestionWorkingFileStrategy>();
        var service = CreateService(
            checkpointRepository,
            stagingRepository,
            workingFileStrategy);

        var response = await service.ResumeAsync("completed-job", CancellationToken.None);

        Assert.Equal(IngestionJobStatus.Success, response.Status);
        Assert.False(response.IsResumeEligible);
        Assert.Equal("This ingestion job is not eligible for resume.", response.Message);
        stagingRepository.Verify(
            repository => repository.GetByJobId(It.IsAny<string>()),
            Times.Never);
        workingFileStrategy.Verify(
            strategy => strategy.ReadAfterAsync(
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResumeAsync_HonorsCancellationBeforeCheckpointLookup()
    {
        var checkpointRepository = new Mock<IIngestionCheckpointRepository>();
        var service = CreateService(checkpointRepository);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ResumeAsync("cancelled-job", cancellation.Token));

        checkpointRepository.Verify(
            repository => repository.GetByJobId(It.IsAny<string>()),
            Times.Never);
    }

    private static IngestionService CreateService(
        Mock<IIngestionCheckpointRepository> checkpointRepository,
        Mock<IIngestionStagingRepository>? stagingRepository = null,
        Mock<IIngestionWorkingFileStrategy>? workingFileStrategy = null)
    {
        IValidator<FileFinding> validator = new FileFindingValidator();

        return new IngestionService(
            NullLogger<IngestionService>.Instance,
            new Mock<IFileFindingRepository>().Object,
            new Mock<IStorageService>().Object,
            validator,
            new Mock<IIngestionJobAuditRepository>().Object,
            new Mock<IRejectedRowRepository>().Object,
            Microsoft.Extensions.Options.Options.Create(new IngestionProcessingOptions
            {
                BatchSize = 100,
                MinBatchSize = 1,
                MaxBatchSize = 1000,
                EnableBatchCheckpointing = true,
                MaxBatchPersistenceRetryCount = 1,
                BatchPersistenceRetryDelayMilliseconds = 0,
                EnableParquetWorkingFile = true,
                LegacyStagingFallbackEnabled = true
            }),
            checkpointRepository.Object,
            (stagingRepository ?? new Mock<IIngestionStagingRepository>()).Object,
            (workingFileStrategy ?? new Mock<IIngestionWorkingFileStrategy>()).Object,
            new Mock<IAuditLogger>().Object);
    }
}
