using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RemediationTool.API.Controllers;
using RemediationTool.Application.Exceptions;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Domain.Enum;
using Xunit;

namespace RemediationTool.API.Tests;

public sealed class IngestionResumeControllerTests
{
    [Fact]
    public async Task Resume_WhenReportUidIsBlank_ReturnsBadRequest()
    {
        var resumeService = new Mock<IIngestionResumeService>();
        var controller = CreateController(resumeService.Object);

        var result = await controller.Resume(" ", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("ReportUID is required.", badRequest.Value);
        resumeService.Verify(
            service => service.ResumeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resume_WhenCheckpointDoesNotExist_ReturnsNotFound()
    {
        var resumeService = new Mock<IIngestionResumeService>();
        resumeService
            .Setup(service => service.ResumeAsync(
                "missing-job",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException(
                "No checkpoint found for ReportUID 'missing-job'."));
        var controller = CreateController(resumeService.Object);

        var result = await controller.Resume(
            "missing-job",
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<IngestionUploadResponse>(notFound.Value);
        Assert.Equal(IngestionJobStatus.Failed, response.Status);
        Assert.False(response.IsResumeEligible);
    }

    [Fact]
    public async Task Resume_WhenRecoveryDataIsUnavailable_ReturnsUnprocessableEntity()
    {
        var failedResponse = new IngestionUploadResponse
        {
            ReportUid = "failed-job",
            JobId = "failed-job",
            Status = IngestionJobStatus.Failed,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Message = "No recovery data is available.",
            IsResumeEligible = false
        };
        var resumeService = new Mock<IIngestionResumeService>();
        resumeService
            .Setup(service => service.ResumeAsync(
                "failed-job",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IngestionResumeDataUnavailableException(failedResponse));
        var controller = CreateController(resumeService.Object);

        var result = await controller.Resume(
            "failed-job",
            CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Same(failedResponse, unprocessable.Value);
    }

    [Fact]
    public async Task Resume_WhenServiceCompletes_ReturnsOk()
    {
        var completedResponse = new IngestionUploadResponse
        {
            ReportUid = "resume-job",
            JobId = "resume-job",
            Status = IngestionJobStatus.Success,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Message = "Ingestion resume completed successfully.",
            IsResumeEligible = false
        };
        var resumeService = new Mock<IIngestionResumeService>();
        resumeService
            .Setup(service => service.ResumeAsync(
                "resume-job",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedResponse);
        var controller = CreateController(resumeService.Object);

        var result = await controller.Resume(
            "resume-job",
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(completedResponse, ok.Value);
    }

    private static IngestionController CreateController(
        IIngestionResumeService resumeService)
    {
        return new IngestionController(
            null!,
            NullLogger<IngestionController>.Instance,
            resumeService);
    }
}
