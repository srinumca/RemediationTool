using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RemediationTool.API.Controllers;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Logging;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Application.Validators;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using Xunit;

namespace RemediationTool.API.Tests;

public sealed class ControllerResponseTests
{
    [Fact]
    public async Task Upload_ValidCsv_ReturnsAcceptedWithReportUid()
    {
        var controller = CreateUploadController();
        var file = CreateFormFile("report.csv", "header\nvalue");

        var result = await controller.Upload(file, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<UploadResponse>(accepted.Value);
        Assert.True(response.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(response.ReportUid));
        Assert.Equal(IngestionJobStatus.Started, response.Status);
    }

    [Fact]
    public async Task Upload_UnsupportedFile_ReturnsBadRequest()
    {
        var controller = CreateUploadController();
        var file = CreateFormFile("report.txt", "content");

        var result = await controller.Upload(file, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<UploadResponse>(badRequest.Value);
        Assert.False(response.IsSuccess);
        Assert.Contains("Only .csv and .xlsx", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ingest_BlankReportUid_ReturnsBadRequest()
    {
        var controller = CreateIngestionController(ValidCsv());

        var result = await controller.Ingest(" ", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("ReportUID is required.", badRequest.Value);
    }

    [Fact]
    public async Task Ingest_UnknownReportUid_ReturnsNotFound()
    {
        var controller = CreateIngestionController(ValidCsv(), jobExists: false);

        var result = await controller.Ingest(TestJobId, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains(TestJobId, Assert.IsType<string>(notFound.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingest_ValidCsv_ReturnsOk()
    {
        var controller = CreateIngestionController(ValidCsv());

        var result = await controller.Ingest(TestJobId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<IngestionUploadResponse>(ok.Value);
        Assert.Equal(IngestionJobStatus.Success, response.Status);
        Assert.Equal(1, response.SuccessCount);
        Assert.Equal(0, response.RejectCount);
    }

    [Fact]
    public async Task Ingest_AllRowsRejected_ReturnsUnprocessableEntity()
    {
        var controller = CreateIngestionController(InvalidCsv());

        var result = await controller.Ingest(TestJobId, CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var response = Assert.IsType<IngestionUploadResponse>(unprocessable.Value);
        Assert.Equal(IngestionJobStatus.Failed, response.Status);
        Assert.Equal(1, response.TotalRecords);
        Assert.Equal(0, response.SuccessCount);
        Assert.Equal(1, response.RejectCount);
    }

    private static UploadController CreateUploadController()
    {
        var storage = new Mock<IStorageService>();
        var jobAuditRepository = new Mock<IIngestionJobAuditRepository>();
        storage
            .Setup(service => service.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new UploadService(
            storage.Object,
            jobAuditRepository.Object,
            NullLogger<UploadService>.Instance,
            Microsoft.Extensions.Options.Options.Create(
                new IngestionProcessingOptions
                {
                    MaxUploadFileSizeMb = 500
                }));

        return new UploadController(
            service,
            NullLogger<UploadController>.Instance);
    }

    private static IngestionController CreateIngestionController(
        string csv,
        bool jobExists = true)
    {
        var audit = new IngestionJobAudit
        {
            ReportUid = TestJobId,
            JobId = TestJobId,
            InboundFileName = "report.csv",
            FileSizeBytes = Encoding.UTF8.GetByteCount(csv),
            FileFormat = "csv",
            S3FolderPath = "2026/07/ING-20260721-CONTROLLER/",
            SourceFilePath = "2026/07/ING-20260721-CONTROLLER/report.csv",
            MetadataJsonPath = "2026/07/ING-20260721-CONTROLLER/report-metadata.json",
            UploadedBy = "controller-test",
            UserName = "controller-test",
            StartedBy = "controller-test",
            StartTimestampUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = IngestionJobStatus.Started
        };

        var fileFindingRepository = new Mock<IFileFindingRepository>();
        var storage = new Mock<IStorageService>();
        var jobAuditRepository = new Mock<IIngestionJobAuditRepository>();
        var rejectedRowRepository = new Mock<IRejectedRowRepository>();
        var checkpointRepository = new Mock<IIngestionCheckpointRepository>();
        var stagingRepository = new Mock<IIngestionStagingRepository>();
        var workingFileStrategy = new Mock<IIngestionWorkingFileStrategy>();
        var auditLogger = new Mock<IAuditLogger>();

        jobAuditRepository
            .Setup(repository => repository.GetByJobId(TestJobId))
            .Returns(jobExists ? audit : null);
        storage
            .Setup(service => service.DownloadAsync(
                audit.SourceFilePath,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        storage
            .Setup(service => service.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        workingFileStrategy
            .SetupGet(strategy => strategy.Format)
            .Returns("parquet");

        var options = new IngestionProcessingOptions
        {
            BatchSize = 100,
            MinBatchSize = 1,
            MaxBatchSize = 1000,
            EnableBatchCheckpointing = true,
            MaxBatchPersistenceRetryCount = 1,
            BatchPersistenceRetryDelayMilliseconds = 0,
            EnableHighVolumeStreaming = false,
            LegacyFallbackEnabled = true,
            EnableParquetWorkingFile = false,
            UseParquetAsPrimaryResumeStore = false,
            LegacyStagingFallbackEnabled = true,
            RejectedRowBatchSize = 100
        };

        IValidator<FileFinding> validator = new FileFindingValidator();
        var service = new IngestionService(
            NullLogger<IngestionService>.Instance,
            fileFindingRepository.Object,
            storage.Object,
            validator,
            jobAuditRepository.Object,
            rejectedRowRepository.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            checkpointRepository.Object,
            stagingRepository.Object,
            workingFileStrategy.Object,
            auditLogger.Object);

        return new IngestionController(
            service,
            NullLogger<IngestionController>.Instance);
    }

    private static IFormFile CreateFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(item => item.Length).Returns(bytes.LongLength);
        file.SetupGet(item => item.FileName).Returns(fileName);
        file.Setup(item => item.OpenReadStream())
            .Returns(() => new MemoryStream(bytes, writable: false));
        return file.Object;
    }

    private static string ValidCsv() =>
        CsvHeader + "\n"
        + @"1,source-1.txt,txt,\\server\share\source-1.txt,Obsolete,SMB,EDG";

    private static string InvalidCsv() =>
        CsvHeader + "\n"
        + @"1,source-1.txt,txt,\\server\share\source-1.txt,Unsupported,SMB,EDG";

    private const string TestJobId = "ING-20260721-CONTROLLER";

    private const string CsvHeader =
        "ID,Finding_File_Name,Finding File Format,Current_File_Location,Finding_Type,Originating_Data_System,Originating_Vendor_Tool";
}
