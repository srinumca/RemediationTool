using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.API.Controllers;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using Xunit;

namespace RemediationTool.API.Tests;

public sealed class UploadControllerValidationTests
{
    [Fact]
    public async Task Upload_MissingFile_ReturnsBadRequestWithoutCallingStorage()
    {
        var storage = new Mock<IStorageService>(MockBehavior.Strict);
        var jobAuditRepository = new Mock<IIngestionJobAuditRepository>(MockBehavior.Strict);
        var uploadService = new UploadService(
            storage.Object,
            jobAuditRepository.Object,
            NullLogger<UploadService>.Instance,
            Options.Create(new IngestionProcessingOptions
            {
                MaxUploadFileSizeMb = 500
            }));
        var controller = new UploadController(
            uploadService,
            NullLogger<UploadController>.Instance);

        var result = await controller.Upload(null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<UploadResponse>(badRequest.Value);
        Assert.False(response.IsSuccess);
        Assert.Equal("A file is required.", response.Message);
        storage.VerifyNoOtherCalls();
        jobAuditRepository.VerifyNoOtherCalls();
    }
}
