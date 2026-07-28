using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.API.Controllers;
using RemediationTool.API.Models;
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
    public async Task Upload_MissingFile_ReturnsBadRequestWithoutCallingDependencies()
    {
        var fixture = new UploadControllerFixture();

        var result = await fixture.Controller.Upload(
            new UploadRequest
            {
                SourceSystem = "NetApp"
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<UploadResponse>(badRequest.Value);
        Assert.False(response.IsSuccess);
        Assert.Equal("A file is required.", response.Message);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Upload_MissingSourceSystem_ReturnsBadRequestWithoutCallingDependencies(
        string? sourceSystem)
    {
        var fixture = new UploadControllerFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");

        var result = await fixture.Controller.Upload(
            new UploadRequest
            {
                File = file,
                SourceSystem = sourceSystem
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<UploadResponse>(badRequest.Value);
        Assert.False(response.IsSuccess);
        Assert.Equal("Source system is required.", response.Message);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_MissingRequest_ReturnsBadRequestWithoutCallingDependencies()
    {
        var fixture = new UploadControllerFixture();

        var result = await fixture.Controller.Upload(null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<UploadResponse>(badRequest.Value);
        Assert.False(response.IsSuccess);
        Assert.Equal("A file is required.", response.Message);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
    }

    private static IFormFile CreateFormFile(string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(item => item.Length).Returns(bytes.LongLength);
        file.SetupGet(item => item.FileName).Returns(fileName);
        file.Setup(item => item.OpenReadStream())
            .Returns(() => new MemoryStream(bytes, writable: false));
        return file.Object;
    }

    private sealed class UploadControllerFixture
    {
        public UploadControllerFixture()
        {
            Storage = new Mock<IStorageService>(MockBehavior.Strict);
            JobAuditRepository = new Mock<IIngestionJobAuditRepository>(MockBehavior.Strict);
            SourceSystemRepository = new Mock<ISourceSystemRepository>(MockBehavior.Strict);

            var uploadService = new UploadService(
                Storage.Object,
                JobAuditRepository.Object,
                SourceSystemRepository.Object,
                NullLogger<UploadService>.Instance,
                Options.Create(new IngestionProcessingOptions
                {
                    MaxUploadFileSizeMb = 500
                }));

            Controller = new UploadController(
                uploadService,
                NullLogger<UploadController>.Instance);
        }

        public Mock<IStorageService> Storage { get; }

        public Mock<IIngestionJobAuditRepository> JobAuditRepository { get; }

        public Mock<ISourceSystemRepository> SourceSystemRepository { get; }

        public UploadController Controller { get; }
    }
}
