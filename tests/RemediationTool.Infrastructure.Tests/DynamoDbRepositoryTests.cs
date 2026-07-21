using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class DynamoDbRepositoryTests
{
    [Fact]
    public void JobAuditRepository_BlankJobId_ReturnsNullWithoutAwsCall()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var repository = CreateJobAuditRepository(client);

        Assert.Null(repository.GetByJobId(" "));
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public void JobAuditRepository_GetByJobId_UsesUidKeyAndMapsResult()
    {
        var client = new Mock<IAmazonDynamoDB>();
        GetItemRequest? captured = null;
        var audit = CreateAudit("job-1");
        client
            .Setup(db => db.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<GetItemRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetItemResponse
            {
                Item = DynamoDbAttributeMap.ToMap(audit)
            });
        var repository = CreateJobAuditRepository(client);

        var result = repository.GetByJobId("job-1");

        Assert.NotNull(result);
        Assert.Equal("job-1", result.JobId);
        Assert.Equal("reports-table", captured?.TableName);
        Assert.Equal("job-1", captured?.Key["uid"].S);
    }

    [Fact]
    public void JobAuditRepository_GetMissingItem_ReturnsNull()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>()
            });
        var repository = CreateJobAuditRepository(client);

        Assert.Null(repository.GetByJobId("missing"));
    }

    [Fact]
    public void JobAuditRepository_AddAndUpdate_PutMappedItemIntoConfiguredTable()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var requests = new List<PutItemRequest>();
        client
            .Setup(db => db.PutItemAsync(
                It.IsAny<PutItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(new PutItemResponse());
        var repository = CreateJobAuditRepository(client);
        var audit = CreateAudit("job-2");

        repository.Add(audit);
        audit.Status = IngestionJobStatus.Success;
        repository.Update(audit);

        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal("reports-table", request.TableName));
        Assert.Equal("job-2", requests[0].Item["uid"].S);
        Assert.Equal("Success", requests[1].Item["status"].S);
        Assert.Throws<ArgumentNullException>(() => repository.Add(null!));
        Assert.Throws<ArgumentNullException>(() => repository.Update(null!));
    }

    [Fact]
    public void JobAuditRepository_AwsFailure_IsPropagated()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("DynamoDB unavailable"));
        var repository = CreateJobAuditRepository(client);

        var exception = Assert.Throws<AmazonDynamoDBException>(() =>
            repository.GetByJobId("job-1"));

        Assert.Equal("DynamoDB unavailable", exception.Message);
    }

    [Fact]
    public void CheckpointRepository_UpsertUpdatesTimestampAndPutsMappedItem()
    {
        var client = new Mock<IAmazonDynamoDB>();
        PutItemRequest? captured = null;
        client
            .Setup(db => db.PutItemAsync(
                It.IsAny<PutItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutItemResponse());
        var repository = new DynamoDbIngestionCheckpointRepository(
            client.Object,
            Options.Create(CreateOptions()));
        var checkpoint = new IngestionCheckpoint
        {
            JobId = "job-3",
            Status = IngestionJobStatus.Failed,
            SuccessCount = 10,
            LastProcessedRecordCount = 5
        };
        var before = DateTime.UtcNow;

        repository.Upsert(checkpoint);

        Assert.InRange(checkpoint.LastCheckpointUtc, before, DateTime.UtcNow);
        Assert.Equal("checkpoints-table", captured?.TableName);
        Assert.Equal("job-3", captured?.Item["jobId"].S);
        Assert.True(captured?.Item["isResumeEligible"].BOOL == true);
        Assert.Throws<ArgumentNullException>(() => repository.Upsert(null!));
    }

    [Fact]
    public void FileFindingRepository_NullAndEmptyInputs_AreNoOps()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var repository = CreateFindingRepository(client, concurrency: 4);

        repository.AddRange(null!);
        repository.AddRange(Array.Empty<FileFinding>());

        client.VerifyNoOtherCalls();
    }

    [Fact]
    public void FileFindingRepository_ChunksAtTwentyFiveAndMapsEveryFinding()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var requests = new ConcurrentBag<BatchWriteItemRequest>();
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(SuccessBatchResponse());
        var repository = CreateFindingRepository(client, concurrency: 4);
        var findings = Enumerable.Range(1, 26)
            .Select(index => CreateFinding($"file-{index}.txt"))
            .ToArray();

        repository.AddRange(findings);

        Assert.Equal(2, requests.Count);
        Assert.Equal(new[] { 1, 25 }, requests
            .Select(request => request.RequestItems["findings-table"].Count)
            .OrderBy(count => count));
        var names = requests
            .SelectMany(request => request.RequestItems["findings-table"])
            .Select(write => write.PutRequest.Item["findingFileName"].S)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(26, names.Count);
        Assert.Contains("file-1.txt", names);
        Assert.Contains("file-26.txt", names);
    }

    [Fact]
    public void FileFindingRepository_RespectsConfiguredParallelism()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var active = 0;
        var maximum = 0;
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (BatchWriteItemRequest _, CancellationToken _) =>
            {
                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximum, current);
                try
                {
                    await Task.Delay(40);
                    return SuccessBatchResponse();
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
        var repository = CreateFindingRepository(client, concurrency: 2);
        var findings = Enumerable.Range(1, 100)
            .Select(index => CreateFinding($"file-{index}.txt"))
            .ToArray();

        repository.AddRange(findings);

        Assert.InRange(maximum, 1, 2);
        client.Verify(
            db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public void FileFindingRepository_RetriesOnlyUnprocessedItems()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var calls = new List<BatchWriteItemRequest>();
        var invocation = 0;
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((BatchWriteItemRequest request, CancellationToken _) =>
            {
                calls.Add(request);
                invocation++;

                if (invocation == 1)
                {
                    var unprocessed = request.RequestItems["findings-table"]
                        .Take(1)
                        .ToList();
                    return Task.FromResult(new BatchWriteItemResponse
                    {
                        UnprocessedItems = new Dictionary<string, List<WriteRequest>>
                        {
                            ["findings-table"] = unprocessed
                        }
                    });
                }

                return Task.FromResult(SuccessBatchResponse());
            });
        var repository = CreateFindingRepository(client, concurrency: 1);

        repository.AddRange(new[]
        {
            CreateFinding("one.txt"),
            CreateFinding("two.txt")
        });

        Assert.Equal(2, calls.Count);
        Assert.Equal(2, calls[0].RequestItems["findings-table"].Count);
        Assert.Single(calls[1].RequestItems["findings-table"]);
    }

    [Fact]
    public void FileFindingRepository_BatchFailure_IsWrappedWithOperationContext()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("throttled"));
        var repository = CreateFindingRepository(client, concurrency: 1);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            repository.AddRange(new[] { CreateFinding("one.txt") }));

        Assert.Contains("Findings batch persistence failed", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void RejectedRowRepository_ChunksAndRetriesUnprocessedRows()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var calls = new List<BatchWriteItemRequest>();
        var invocation = 0;
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((BatchWriteItemRequest request, CancellationToken _) =>
            {
                calls.Add(request);
                invocation++;
                if (invocation == 1)
                {
                    return Task.FromResult(new BatchWriteItemResponse
                    {
                        UnprocessedItems = new Dictionary<string, List<WriteRequest>>
                        {
                            ["rejected-table"] = request.RequestItems["rejected-table"]
                                .Take(1)
                                .ToList()
                        }
                    });
                }

                return Task.FromResult(SuccessBatchResponse());
            });
        var repository = new DynamoDbRejectedRowRepository(
            client.Object,
            Options.Create(CreateOptions()),
            NullLogger<DynamoDbRejectedRowRepository>.Instance);
        var rows = Enumerable.Range(1, 26)
            .Select(index => new RejectedRowDetail
            {
                Id = $"row-{index}",
                Uid = "job-1",
                ErrorReason = "invalid"
            })
            .ToList();

        repository.AddRange(rows);

        Assert.Equal(3, calls.Count);
        Assert.Equal(25, calls[0].RequestItems["rejected-table"].Count);
        Assert.Single(calls[1].RequestItems["rejected-table"]);
        Assert.Single(calls[2].RequestItems["rejected-table"]);
    }

    [Fact]
    public void RejectedRowRepository_NullAndEmptyInputs_AreNoOps()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var repository = new DynamoDbRejectedRowRepository(
            client.Object,
            Options.Create(CreateOptions()),
            NullLogger<DynamoDbRejectedRowRepository>.Instance);

        repository.AddRange(null!);
        repository.AddRange(new List<RejectedRowDetail>());

        client.VerifyNoOtherCalls();
    }

    private static DynamoDbIngestionJobAuditRepository CreateJobAuditRepository(
        Mock<IAmazonDynamoDB> client)
        => new(
            client.Object,
            Options.Create(CreateOptions()),
            NullLogger<DynamoDbIngestionJobAuditRepository>.Instance);

    private static DynamoDbFileFindingRepository CreateFindingRepository(
        Mock<IAmazonDynamoDB> client,
        int concurrency)
    {
        var options = CreateOptions();
        options.MaxBatchWriteConcurrency = concurrency;
        return new DynamoDbFileFindingRepository(
            client.Object,
            Options.Create(options),
            NullLogger<DynamoDbFileFindingRepository>.Instance);
    }

    private static DynamoDbOptions CreateOptions()
        => new()
        {
            FindingsTableName = "findings-table",
            JobAuditTableName = "reports-table",
            RejectedRowsTableName = "rejected-table",
            CheckpointsTableName = "checkpoints-table",
            StagedFindingsTableName = "staging-table",
            MaxBatchWriteConcurrency = 4
        };

    private static IngestionJobAudit CreateAudit(string jobId)
        => new()
        {
            JobId = jobId,
            ReportUid = jobId,
            InboundFileName = "report.csv",
            Status = IngestionJobStatus.Started,
            StartTimestampUtc = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)
        };

    private static FileFinding CreateFinding(string fileName)
        => new()
        {
            RecordVersionId = "1",
            IngestionJobId = "job-1",
            FindingFileName = fileName,
            FindingFileFormat = "txt",
            CurrentFileLocation = $"/source/{fileName}",
            FindingType = "Obsolete",
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG"
        };

    private static BatchWriteItemResponse SuccessBatchResponse()
        => new()
        {
            UnprocessedItems = new Dictionary<string, List<WriteRequest>>()
        };

    private static void UpdateMaximum(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current)
                return;

            if (Interlocked.CompareExchange(ref target, candidate, current) == current)
                return;
        }
    }
}
