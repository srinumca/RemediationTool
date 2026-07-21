using System.Text.Json;
using System.Text.Json.Serialization;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class SharedDataRepositoryCollection
{
    public const string CollectionName = "AppContext data repositories";
}

[Collection(SharedDataRepositoryCollection.CollectionName)]
public sealed class SharedDataRepositoryTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
    private readonly string _checkpointPath;
    private readonly string _stagingPath;

    public SharedDataRepositoryTests()
    {
        _checkpointPath = Path.Combine(_dataDirectory, "ingestion-checkpoints.json");
        _stagingPath = Path.Combine(_dataDirectory, "ingestion-staged-findings.json");
        Cleanup();
    }

    [Fact]
    public void CheckpointRepository_ConstructorCreatesEmptyStore()
    {
        _ = new JsonIngestionCheckpointRepository();

        Assert.True(File.Exists(_checkpointPath));
        Assert.Equal("[]", File.ReadAllText(_checkpointPath).Trim());
    }

    [Fact]
    public void CheckpointRepository_UpsertInsertsAndReplacesCaseInsensitively()
    {
        var repository = new JsonIngestionCheckpointRepository();
        var before = DateTime.UtcNow;
        repository.Upsert(new IngestionCheckpoint
        {
            JobId = "JOB-1",
            Status = IngestionJobStatus.Started,
            SuccessCount = 10,
            LastProcessedRecordCount = 2
        });
        var inserted = ReadCheckpoints();
        var first = Assert.Single(inserted);
        Assert.InRange(first.CreatedAtUtc, before, DateTime.UtcNow);
        Assert.InRange(first.LastCheckpointUtc, before, DateTime.UtcNow);

        repository.Upsert(new IngestionCheckpoint
        {
            JobId = "job-1",
            Status = IngestionJobStatus.Failed,
            SuccessCount = 10,
            LastProcessedRecordCount = 5,
            FailureReason = "failure"
        });

        var updated = Assert.Single(ReadCheckpoints());
        Assert.Equal("job-1", updated.JobId);
        Assert.Equal(IngestionJobStatus.Failed, updated.Status);
        Assert.Equal(5, updated.LastProcessedRecordCount);
        Assert.Equal("failure", updated.FailureReason);
        Assert.True(updated.IsResumeEligible);
    }

    [Fact]
    public void CheckpointRepository_NullCheckpoint_Throws()
    {
        var repository = new JsonIngestionCheckpointRepository();

        Assert.Throws<ArgumentNullException>(() => repository.Upsert(null!));
    }

    [Fact]
    public void StagingRepository_SaveAssignsSequenceAndReplacesSameJobOnly()
    {
        var repository = new JsonIngestionStagingRepository();
        repository.SaveValidFindings(
            "job-1",
            new List<FileFinding>
            {
                CreateFinding("one.txt"),
                CreateFinding("two.txt")
            });
        repository.SaveValidFindings(
            "job-2",
            new List<FileFinding>
            {
                CreateFinding("other.txt")
            });

        var initial = ReadStagedFindings();
        Assert.Equal(3, initial.Count);
        Assert.Equal(new[] { 1, 2 }, initial
            .Where(item => item.JobId == "job-1")
            .Select(item => item.SequenceNumber));

        repository.SaveValidFindings(
            "JOB-1",
            new List<FileFinding>
            {
                CreateFinding("replacement.txt")
            });

        var replaced = ReadStagedFindings();
        Assert.Equal(2, replaced.Count);
        var job1 = Assert.Single(replaced, item =>
            item.JobId.Equals("JOB-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, job1.SequenceNumber);
        Assert.Equal("replacement.txt", job1.Finding.FindingFileName);
        Assert.Single(replaced, item => item.JobId == "job-2");
    }

    [Fact]
    public void StagingRepository_DeleteIsCaseInsensitiveAndBlankIsNoOp()
    {
        var repository = new JsonIngestionStagingRepository();
        repository.SaveValidFindings(
            "job-1",
            new List<FileFinding> { CreateFinding("one.txt") });
        repository.SaveValidFindings(
            "job-2",
            new List<FileFinding> { CreateFinding("two.txt") });

        repository.DeleteByJobId("JOB-1");
        repository.DeleteByJobId(" ");
        repository.DeleteByJobId("missing");

        var remaining = ReadStagedFindings();
        var item = Assert.Single(remaining);
        Assert.Equal("job-2", item.JobId);
    }

    [Fact]
    public void StagingRepository_ValidatesJobAndIgnoresEmptyRows()
    {
        var repository = new JsonIngestionStagingRepository();

        Assert.Throws<ArgumentException>(() =>
            repository.SaveValidFindings(" ", new List<FileFinding> { CreateFinding("one.txt") }));
        repository.SaveValidFindings("job-1", null!);
        repository.SaveValidFindings("job-1", new List<FileFinding>());

        Assert.Empty(ReadStagedFindings());
    }

    public void Dispose() => Cleanup();

    private List<IngestionCheckpoint> ReadCheckpoints()
        => JsonSerializer.Deserialize<List<IngestionCheckpoint>>(
               File.ReadAllText(_checkpointPath),
               JsonOptions)
           ?? new List<IngestionCheckpoint>();

    private List<IngestionStagedFinding> ReadStagedFindings()
        => JsonSerializer.Deserialize<List<IngestionStagedFinding>>(
               File.ReadAllText(_stagingPath),
               JsonOptions)
           ?? new List<IngestionStagedFinding>();

    private void Cleanup()
    {
        Directory.CreateDirectory(_dataDirectory);
        DeleteIfExists(_checkpointPath);
        DeleteIfExists(_stagingPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static FileFinding CreateFinding(string name)
        => new()
        {
            FindingFileName = name,
            FindingFileFormat = "txt",
            CurrentFileLocation = $"/source/{name}",
            FindingType = "Obsolete",
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG"
        };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
