using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class UploadServiceTests
{
    private const string ValidSourceSystem = "NetApp";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UploadAsync_RejectsMissingOrEmptyFile(string? content)
    {
        var fixture = new UploadFixture();
        IFormFile? file = content is null
            ? null
            : CreateFormFile("report.csv", content);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file!, ValidSourceSystem, CancellationToken.None));

        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("report.txt")]
    [InlineData("report")]
    [InlineData("report.json")]
    public async Task UploadAsync_RejectsUnsupportedFileType(string fileName)
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile(fileName, "content");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file, ValidSourceSystem, CancellationToken.None));

        Assert.Contains("Only .csv and .xlsx", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadAsync_RejectsFileAboveConfiguredLimit()
    {
        var fixture = new UploadFixture(maxUploadFileSizeMb: 1);
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(item => item.Length).Returns(2L * 1024 * 1024);
        file.SetupGet(item => item.FileName).Returns("large.csv");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file.Object, ValidSourceSystem, CancellationToken.None));

        Assert.Contains("1 MB", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadAsync_RejectsMissingSourceSystem(string? sourceSystem)
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file, sourceSystem, CancellationToken.None));

        Assert.Equal("Source system is required.", exception.Message);
        fixture.SourceSystemRepository.VerifyNoOtherCalls();
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadAsync_RejectsUnknownSourceSystemBeforeStorage()
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");
        fixture.SourceSystemRepository
            .Setup(repository => repository.GetBySourceSystemAsync(
                "Unknown",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SourceSystemDefinition?)null);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file, "Unknown", CancellationToken.None));

        Assert.Contains("invalid or disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadAsync_RejectsDisabledSourceSystemBeforeStorage()
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");
        fixture.SourceSystemRepository
            .Setup(repository => repository.GetBySourceSystemAsync(
                "Disabled",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceSystemDefinition
            {
                SourceSystem = "Disabled",
                IsEnabled = false
            });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file, "Disabled", CancellationToken.None));

        Assert.Contains("invalid or disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("report.csv", "csv")]
    [InlineData("report.XLSX", "xlsx")]
    public async Task UploadAsync_StoresSourceMetadataAndJobAudit(
        string fileName,
        string expectedFormat)
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile(fileName, "header\nvalue");
        var uploads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        IngestionJobAudit? storedAudit = null;

        fixture.Storage
            .Setup(storage => storage.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Stream data, CancellationToken _) =>
            {
                using var copy = new MemoryStream();
                data.CopyTo(copy);
                uploads[key] = copy.ToArray();
                return Task.CompletedTask;
            });
        fixture.JobAuditRepository
            .Setup(repository => repository.Add(It.IsAny<IngestionJobAudit>()))
            .Callback<IngestionJobAudit>(audit => storedAudit = audit);

        var response = await fixture.Service.UploadAsync(
            file,
            $" {ValidSourceSystem} ",
            CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(response.ReportUid));
        Assert.Equal(response.ReportUid, response.JobId);
        Assert.Equal(fileName, response.InboundFileName);
        Assert.Equal(ValidSourceSystem, response.SourceSystem);
        Assert.Equal(expectedFormat, storedAudit?.FileFormat);
        Assert.Equal(IngestionJobStatus.Started, response.Status);
        Assert.NotNull(response.SourceFilePath);
        Assert.NotNull(response.MetadataJsonPath);
        Assert.Equal(2, uploads.Count);
        Assert.True(uploads.ContainsKey(response.SourceFilePath!));
        Assert.True(uploads.ContainsKey(response.MetadataJsonPath!));
        Assert.Equal("header\nvalue", Encoding.UTF8.GetString(uploads[response.SourceFilePath!]));

        using var metadata = JsonDocument.Parse(uploads[response.MetadataJsonPath!]);
        Assert.Equal(response.ReportUid, metadata.RootElement.GetProperty("ReportUid").GetString());
        Assert.Equal(fileName, metadata.RootElement.GetProperty("InboundFileName").GetString());
        Assert.Equal(ValidSourceSystem, metadata.RootElement.GetProperty("SourceSystem").GetString());
        Assert.Equal("Uploaded", metadata.RootElement.GetProperty("Status").GetString());

        Assert.NotNull(storedAudit);
        Assert.Equal(response.ReportUid, storedAudit.ReportUid);
        Assert.Equal(response.SourceFilePath, storedAudit.SourceFilePath);
        Assert.Equal(response.MetadataJsonPath, storedAudit.MetadataJsonPath);
        Assert.Equal(ValidSourceSystem, storedAudit.SourceSystem);
        Assert.Equal("system", storedAudit.UploadedBy);
        Assert.Equal(IngestionJobStatus.Started, storedAudit.Status);

        fixture.SourceSystemRepository.Verify(
            repository => repository.GetBySourceSystemAsync(
                ValidSourceSystem,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Storage.Verify(
            storage => storage.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        fixture.JobAuditRepository.Verify(
            repository => repository.Add(It.IsAny<IngestionJobAudit>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_PropagatesStorageFailureWithoutCreatingAudit()
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");
        fixture.Storage
            .Setup(storage => storage.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("storage unavailable"));

        var exception = await Assert.ThrowsAsync<IOException>(
            () => fixture.Service.UploadAsync(file, ValidSourceSystem, CancellationToken.None));

        Assert.Equal("storage unavailable", exception.Message);
        fixture.JobAuditRepository.Verify(
            repository => repository.Add(It.IsAny<IngestionJobAudit>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadAsync_HonorsCancellationBeforeSourceSystemLookupOrStorage()
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.UploadAsync(file, ValidSourceSystem, cancellation.Token));

        fixture.SourceSystemRepository.VerifyNoOtherCalls();
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
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

    private sealed class UploadFixture
    {
        public UploadFixture(int maxUploadFileSizeMb = 500)
        {
            Storage = new Mock<IStorageService>(MockBehavior.Strict);
            JobAuditRepository = new Mock<IIngestionJobAuditRepository>(MockBehavior.Strict);
            SourceSystemRepository = new Mock<ISourceSystemRepository>(MockBehavior.Strict);
            SourceSystemRepository
                .Setup(repository => repository.GetBySourceSystemAsync(
                    ValidSourceSystem,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SourceSystemDefinition
                {
                    SourceSystem = ValidSourceSystem,
                    DataSystem = ValidSourceSystem,
                    DisplayName = "NetApp File Server",
                    IsEnabled = true,
                    OriginatingDataSystem = "smb"
                });

            Service = new UploadService(
                Storage.Object,
                JobAuditRepository.Object,
                SourceSystemRepository.Object,
                NullLogger<UploadService>.Instance,
                Microsoft.Extensions.Options.Options.Create(
                    new IngestionProcessingOptions
                    {
                        MaxUploadFileSizeMb = maxUploadFileSizeMb
                    }));
        }

        public Mock<IStorageService> Storage { get; }

        public Mock<IIngestionJobAuditRepository> JobAuditRepository { get; }

        public Mock<ISourceSystemRepository> SourceSystemRepository { get; }

        public UploadService Service { get; }
    }
}
