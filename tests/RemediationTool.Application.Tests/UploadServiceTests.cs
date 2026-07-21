using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
            () => fixture.Service.UploadAsync(file!, CancellationToken.None));

        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
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
            () => fixture.Service.UploadAsync(file, CancellationToken.None));

        Assert.Contains("Only .csv and .xlsx", exception.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Storage.VerifyNoOtherCalls();
        fixture.JobAuditRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadAsync_RejectsFileAboveConfiguredLimit()
    {
        var fixture = new UploadFixture(maxUploadFileSizeMb: 1);
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(item => item.Length).Returns(2L * 1024 * 1024);
        file.SetupGet(item => item.FileName).Returns("large.csv");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Service.UploadAsync(file.Object, CancellationToken.None));

        Assert.Contains("1 MB", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        var response = await fixture.Service.UploadAsync(file, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(response.ReportUid));
        Assert.Equal(response.ReportUid, response.JobId);
        Assert.Equal(fileName, response.InboundFileName);
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
        Assert.Equal("Uploaded", metadata.RootElement.GetProperty("Status").GetString());

        Assert.NotNull(storedAudit);
        Assert.Equal(response.ReportUid, storedAudit.ReportUid);
        Assert.Equal(response.SourceFilePath, storedAudit.SourceFilePath);
        Assert.Equal(response.MetadataJsonPath, storedAudit.MetadataJsonPath);
        Assert.Equal("system", storedAudit.UploadedBy);
        Assert.Equal(IngestionJobStatus.Started, storedAudit.Status);

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
            () => fixture.Service.UploadAsync(file, CancellationToken.None));

        Assert.Equal("storage unavailable", exception.Message);
        fixture.JobAuditRepository.Verify(
            repository => repository.Add(It.IsAny<IngestionJobAudit>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadAsync_HonorsCancellationBeforeStorageStarts()
    {
        var fixture = new UploadFixture();
        var file = CreateFormFile("report.csv", "header\nvalue");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.UploadAsync(file, cancellation.Token));

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
            Service = new UploadService(
                Storage.Object,
                JobAuditRepository.Object,
                NullLogger<UploadService>.Instance,
                Options.Create(new IngestionProcessingOptions
                {
                    MaxUploadFileSizeMb = maxUploadFileSizeMb
                }));
        }

        public Mock<IStorageService> Storage { get; }

        public Mock<IIngestionJobAuditRepository> JobAuditRepository { get; }

        public UploadService Service { get; }
    }
}
