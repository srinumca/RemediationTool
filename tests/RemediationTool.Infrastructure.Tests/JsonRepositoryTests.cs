using System.Text.Json;
using System.Text.Json.Serialization;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class JsonRepositoryTests : IClassFixture<TemporaryDirectoryFixture>
{
    private static readonly JsonSerializerOptions EnumJsonOptions = CreateJsonOptions();
    private readonly TemporaryDirectoryFixture _fixture;

    public JsonRepositoryTests(TemporaryDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void JsonFileHelper_WriteAndRead_RoundTripsUnicodeAndOverwrites()
    {
        var directory = CreateCaseDirectory();
        var path = Path.Combine(directory, "data.json");

        JsonFileHelper.WriteAllText(path, "first-✓", maxAttempts: 1, delayMs: 0);
        Assert.Equal("first-✓", JsonFileHelper.ReadAllText(path, maxAttempts: 1, delayMs: 0));

        JsonFileHelper.WriteAllText(path, "second", maxAttempts: 0, delayMs: -1);
        Assert.Equal("second", JsonFileHelper.ReadAllText(path, maxAttempts: 0, delayMs: -1));
    }

    [Fact]
    public void JsonFileHelper_ReadMissingFile_PropagatesAfterConfiguredAttempts()
    {
        var path = Path.Combine(CreateCaseDirectory(), "missing.json");

        Assert.Throws<FileNotFoundException>(() =>
            JsonFileHelper.ReadAllText(path, maxAttempts: 1, delayMs: 0));
    }

    [Fact]
    public void FileFindingRepository_CreatesFileAndAppendsFindings()
    {
        var root = CreateCaseDirectory();
        var repository = new JsonFileFindingRepository(
            _fixture.Configuration(("Persistence:JsonRootPath", root)));
        var path = Path.Combine(root, "metadata.json");

        repository.AddRange(new[]
        {
            CreateFinding("one.txt", "job-1", FileStatus.NotYetStarted)
        });
        repository.AddRange(new[]
        {
            CreateFinding("two.txt", "job-1", FileStatus.Quarantined)
        });

        var findings = JsonSerializer.Deserialize<List<FileFinding>>(
            File.ReadAllText(path),
            EnumJsonOptions);
        Assert.NotNull(findings);
        Assert.Collection(
            findings,
            first => Assert.Equal("one.txt", first.FindingFileName),
            second =>
            {
                Assert.Equal("two.txt", second.FindingFileName);
                Assert.Equal(FileStatus.Quarantined, second.Status);
            });
    }

    [Fact]
    public void FileFindingRepository_NullAndEmptyInput_AreNoOps()
    {
        var root = CreateCaseDirectory();
        var repository = new JsonFileFindingRepository(
            _fixture.Configuration(("Persistence:JsonRootPath", root)));
        var path = Path.Combine(root, "metadata.json");

        repository.AddRange(null!);
        repository.AddRange(Array.Empty<FileFinding>());

        Assert.Equal("[]", File.ReadAllText(path).Trim());
    }

    [Fact]
    public async Task FileFindingRepository_ConcurrentAdds_DoNotLoseRecords()
    {
        var root = CreateCaseDirectory();
        var repository = new JsonFileFindingRepository(
            _fixture.Configuration(("Persistence:JsonRootPath", root)));
        var tasks = Enumerable.Range(1, 20)
            .Select(index => Task.Run(() =>
                repository.AddRange(new[]
                {
                    CreateFinding($"file-{index}.txt", "job-concurrent", FileStatus.NotYetStarted)
                })))
            .ToArray();

        await Task.WhenAll(tasks);

        var findings = JsonSerializer.Deserialize<List<FileFinding>>(
            File.ReadAllText(Path.Combine(root, "metadata.json")),
            EnumJsonOptions);
        Assert.NotNull(findings);
        Assert.Equal(20, findings.Count);
        Assert.Equal(20, findings.Select(item => item.FindingFileName).Distinct().Count());
    }

    [Fact]
    public void JobAuditRepository_AddGetAndUpdate_AreCaseInsensitive()
    {
        var root = CreateCaseDirectory();
        var repository = new JsonIngestionJobAuditRepository(
            _fixture.Configuration(("Persistence:JsonRootPath", root)));
        var audit = new IngestionJobAudit
        {
            JobId = "JOB-ABC",
            ReportUid = "JOB-ABC",
            InboundFileName = "report.csv",
            Status = IngestionJobStatus.Started
        };

        repository.Add(audit);
        var loaded = repository.GetByJobId("job-abc");
        Assert.NotNull(loaded);
        Assert.Equal(IngestionJobStatus.Started, loaded.Status);

        audit.Status = IngestionJobStatus.Success;
        audit.SuccessCount = 5;
        repository.Update(audit);

        loaded = repository.GetByJobId("JOB-ABC");
        Assert.NotNull(loaded);
        Assert.Equal(IngestionJobStatus.Success, loaded.Status);
        Assert.Equal(5, loaded.SuccessCount);
        Assert.Single(ReadJsonArray(Path.Combine(root, "ingestion-job-audit.json")));
    }

    [Fact]
    public void JobAuditRepository_UpdateMissing_AddsRecordAndBlankLookupReturnsNull()
    {
        var root = CreateCaseDirectory();
        var repository = new JsonIngestionJobAuditRepository(
            _fixture.Configuration(("Persistence:JsonRootPath", root)));

        repository.Update(new IngestionJobAudit
        {
            JobId = "job-new",
            ReportUid = "job-new"
        });

        Assert.NotNull(repository.GetByJobId("job-new"));
        Assert.Null(repository.GetByJobId(" "));
        Assert.Throws<ArgumentNullException>(() => repository.Add(null!));
        Assert.Throws<ArgumentNullException>(() => repository.Update(null!));
    }

    [Fact]
    public void RejectedRowRepository_AppendsRowsAndIgnoresEmptyInput()
    {
        var root = CreateCaseDirectory();
        var repository = new JsonRejectedRowRepository(
            _fixture.Configuration(("Persistence:JsonRootPath", root)));
        var path = Path.Combine(root, "rejected-rows.json");

        repository.AddRange(null!);
        repository.AddRange(new List<RejectedRowDetail>());
        repository.AddRange(new List<RejectedRowDetail>
        {
            new() { Id = "row-1", Uid = "job-1", ErrorReason = "bad value" },
            new() { Id = "row-2", Uid = "job-1", ErrorReason = "missing value" }
        });

        var rows = JsonSerializer.Deserialize<List<RejectedRowDetail>>(File.ReadAllText(path));
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "row-1", "row-2" }, rows.Select(row => row.Id));
    }

    private string CreateCaseDirectory()
    {
        var path = Path.Combine(_fixture.RootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static FileFinding CreateFinding(
        string fileName,
        string jobId,
        FileStatus status)
        => new()
        {
            IngestionJobId = jobId,
            FindingFileName = fileName,
            FindingFileFormat = "txt",
            CurrentFileLocation = $"/source/{fileName}",
            FindingType = "Obsolete",
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG",
            Status = status
        };

    private static JsonElement.ArrayEnumerator ReadJsonArray(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone().EnumerateArray();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
