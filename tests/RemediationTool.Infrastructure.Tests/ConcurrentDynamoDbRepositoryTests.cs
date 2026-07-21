using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class ConcurrentDynamoDbRepositoryTests
{
    [Fact]
    public async Task Findings_BoundedMode_ChunksMapsAndAwaitsEveryBatch()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var requests = new ConcurrentBag<BatchWriteItemRequest>();
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(Success());
        var repository = CreateConcurrentFindings(client, bounded: true, concurrency: 2);
        var findings = Enumerable.Range(1, 26)
            .Select(index => Finding($"file-{index}.txt"))
            .ToArray();

        await repository.AddRangeAsync(findings);

        Assert.Equal(2, requests.Count);
        Assert.Equal(new[] { 1, 25 }, requests
            .Select(request => request.RequestItems["findings-table"].Count)
            .OrderBy(count => count));
        Assert.Equal(
            26,
            requests
                .SelectMany(request => request.RequestItems["findings-table"])
                .Select(item => item.PutRequest.Item["findingFileName"].S)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task Findings_LegacyMode_DelegatesToInnerRepository()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success());
        var repository = CreateConcurrentFindings(client, bounded: false, concurrency: 4);

        await repository.AddRangeAsync(new[] { Finding("legacy.txt") });

        client.Verify(
            db => db.BatchWriteItemAsync(
                It.Is<BatchWriteItemRequest>(request =>
                    request.RequestItems["findings-table"].Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Findings_NullEmptyAndCancellation_AreHandledBeforeAws()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var repository = CreateConcurrentFindings(client, bounded: true, concurrency: 2);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await repository.AddRangeAsync(null!);
        await repository.AddRangeAsync(Array.Empty<FileFinding>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.AddRangeAsync(
                new[] { Finding("cancelled.txt") },
                cancellation.Token));

        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RejectedRows_BoundedMode_UsesApplicationAndDynamoBatchLimits()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var requests = new List<BatchWriteItemRequest>();
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) =>
            {
                lock (requests)
                    requests.Add(request);
            })
            .ReturnsAsync(Success());
        var repository = CreateConcurrentRejectedRows(
            client,
            bounded: true,
            concurrency: 2,
            applicationBatchSize: 30);
        var rows = Enumerable.Range(1, 55)
            .Select(index => RejectedRow($"row-{index}"))
            .ToArray();

        await repository.AddRangeAsync(rows);

        Assert.Equal(3, requests.Count);
        Assert.Equal(new[] { 5, 25, 25 }, requests
            .Select(request => request.RequestItems["rejected-table"].Count)
            .OrderBy(count => count));
        Assert.Equal(
            55,
            requests
                .SelectMany(request => request.RequestItems["rejected-table"])
                .Select(item => item.PutRequest.Item["id"].S)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task RejectedRows_LegacyMode_DelegatesAndCancellationStopsEarly()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success());
        var legacy = CreateConcurrentRejectedRows(client, bounded: false, concurrency: 2);

        await legacy.AddRangeAsync(new[] { RejectedRow("legacy-row") });

        client.Verify(
            db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        client.Invocations.Clear();
        var bounded = CreateConcurrentRejectedRows(client, bounded: true, concurrency: 2);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bounded.AddRangeAsync(
                new[] { RejectedRow("cancelled-row") },
                cancellation.Token));
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Staging_BoundedSave_DeletesExistingThenWritesOrderedRowsWithTtl()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var requests = new List<BatchWriteItemRequest>();
        client
            .Setup(db => db.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>(),
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()
            });
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) =>
            {
                lock (requests)
                    requests.Add(request);
            })
            .ReturnsAsync(Success());
        var repository = CreateConcurrentStaging(client, bounded: true, concurrency: 2);
        var findings = Enumerable.Range(1, 26)
            .Select(index => Finding($"stage-{index}.txt"))
            .ToArray();

        await repository.SaveValidFindingsAsync("job-stage", findings);

        Assert.Equal(2, requests.Count);
        var items = requests
            .SelectMany(request => request.RequestItems["staging-table"])
            .Select(write => write.PutRequest.Item)
            .OrderBy(item => int.Parse(item["sequenceNumber"].N))
            .ToList();
        Assert.Equal(26, items.Count);
        Assert.Equal("1", items[0]["sequenceNumber"].N);
        Assert.Equal("26", items[^1]["sequenceNumber"].N);
        Assert.All(items, item =>
        {
            Assert.Equal("job-stage", item["jobId"].S);
            Assert.True(long.Parse(item["ExpiresAt"].N) > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.NotEmpty(item["finding"].M);
        });
    }

    [Fact]
    public async Task Staging_BoundedDelete_QueriesAndDeletesProjectedKeys()
    {
        var client = new Mock<IAmazonDynamoDB>();
        QueryRequest? query = null;
        BatchWriteItemRequest? deleteBatch = null;
        client
            .Setup(db => db.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<QueryRequest, CancellationToken>((request, _) => query = request)
            .ReturnsAsync(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    StagingKey("job-delete", 1),
                    StagingKey("job-delete", 2)
                },
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()
            });
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) =>
                deleteBatch = request)
            .ReturnsAsync(Success());
        var repository = CreateConcurrentStaging(client, bounded: true, concurrency: 2);

        await repository.DeleteByJobIdAsync("job-delete");

        Assert.Equal("staging-table", query?.TableName);
        Assert.Equal("jobId = :jobId", query?.KeyConditionExpression);
        Assert.Equal("job-delete", query?.ExpressionAttributeValues[":jobId"].S);
        Assert.Equal("jobId, sequenceNumber", query?.ProjectionExpression);
        Assert.NotNull(deleteBatch);
        var deletes = deleteBatch.RequestItems["staging-table"];
        Assert.Equal(2, deletes.Count);
        Assert.All(deletes, write => Assert.NotNull(write.DeleteRequest));
    }

    [Fact]
    public async Task Staging_ValidatesJobAndHonorsCancellation()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var repository = CreateConcurrentStaging(client, bounded: true, concurrency: 2);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.SaveValidFindingsAsync(" ", new[] { Finding("one.txt") }));
        await repository.SaveValidFindingsAsync("job", Array.Empty<FileFinding>());
        await repository.DeleteByJobIdAsync(" ");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.DeleteByJobIdAsync("job", cancellation.Token));
        client.VerifyNoOtherCalls();
    }

    private static ConcurrentDynamoDbFileFindingRepository CreateConcurrentFindings(
        Mock<IAmazonDynamoDB> client,
        bool bounded,
        int concurrency)
    {
        var dynamoOptions = DynamoOptions();
        var processing = ProcessingOptions(bounded, concurrency);
        var inner = new DynamoDbFileFindingRepository(
            client.Object,
            Options.Create(dynamoOptions),
            NullLogger<DynamoDbFileFindingRepository>.Instance);
        return new ConcurrentDynamoDbFileFindingRepository(
            inner,
            client.Object,
            Options.Create(dynamoOptions),
            Options.Create(processing),
            NullLogger<ConcurrentDynamoDbFileFindingRepository>.Instance);
    }

    private static ConcurrentDynamoDbRejectedRowRepository CreateConcurrentRejectedRows(
        Mock<IAmazonDynamoDB> client,
        bool bounded,
        int concurrency,
        int applicationBatchSize = 5000)
    {
        var dynamoOptions = DynamoOptions();
        var processing = ProcessingOptions(bounded, concurrency);
        processing.RejectedRowBatchSize = applicationBatchSize;
        processing.MaxBatchSize = 10_000;
        var inner = new DynamoDbRejectedRowRepository(
            client.Object,
            Options.Create(dynamoOptions),
            NullLogger<DynamoDbRejectedRowRepository>.Instance);
        return new ConcurrentDynamoDbRejectedRowRepository(
            inner,
            client.Object,
            Options.Create(dynamoOptions),
            Options.Create(processing),
            NullLogger<ConcurrentDynamoDbRejectedRowRepository>.Instance);
    }

    private static ConcurrentDynamoDbIngestionStagingRepository CreateConcurrentStaging(
        Mock<IAmazonDynamoDB> client,
        bool bounded,
        int concurrency)
    {
        var dynamoOptions = DynamoOptions();
        var processing = ProcessingOptions(bounded, concurrency);
        var inner = new DynamoDbIngestionStagingRepository(
            client.Object,
            Options.Create(dynamoOptions),
            NullLogger<DynamoDbIngestionStagingRepository>.Instance);
        return new ConcurrentDynamoDbIngestionStagingRepository(
            inner,
            client.Object,
            Options.Create(dynamoOptions),
            Options.Create(processing),
            NullLogger<ConcurrentDynamoDbIngestionStagingRepository>.Instance);
    }

    private static DynamoDbOptions DynamoOptions()
        => new()
        {
            FindingsTableName = "findings-table",
            RejectedRowsTableName = "rejected-table",
            StagedFindingsTableName = "staging-table",
            MaxBatchWriteConcurrency = 4
        };

    private static IngestionProcessingOptions ProcessingOptions(
        bool bounded,
        int concurrency)
        => new()
        {
            EnableBoundedDynamoDbConcurrency = bounded,
            DynamoDbWriteConcurrency = concurrency,
            RejectedRowBatchSize = 5000,
            MaxBatchSize = 10_000
        };

    private static FileFinding Finding(string fileName)
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

    private static RejectedRowDetail RejectedRow(string id)
        => new()
        {
            Id = id,
            Uid = "job-1",
            ErrorReason = "invalid"
        };

    private static Dictionary<string, AttributeValue> StagingKey(
        string jobId,
        int sequenceNumber)
        => new()
        {
            ["jobId"] = new AttributeValue { S = jobId },
            ["sequenceNumber"] = new AttributeValue { N = sequenceNumber.ToString() }
        };

    private static BatchWriteItemResponse Success()
        => new()
        {
            UnprocessedItems = new Dictionary<string, List<WriteRequest>>()
        };
}
