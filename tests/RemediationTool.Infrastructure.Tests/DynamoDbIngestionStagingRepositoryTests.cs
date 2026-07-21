using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class DynamoDbIngestionStagingRepositoryTests
{
    [Fact]
    public void SaveValidFindings_DeletesExistingAndWritesOrderedBatches()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var writes = new ConcurrentBag<BatchWriteItemRequest>();
        client
            .Setup(db => db.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyQuery());
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) => writes.Add(request))
            .ReturnsAsync(Success());
        var repository = CreateRepository(client, concurrency: 2);
        var findings = Enumerable.Range(1, 26)
            .Select(index => Finding($"file-{index}.txt"))
            .ToList();

        repository.SaveValidFindings("job-1", findings);

        client.Verify(
            db => db.QueryAsync(
                It.Is<QueryRequest>(request =>
                    request.TableName == "staging-table"
                    && request.ExpressionAttributeValues[":jobId"].S == "job-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(2, writes.Count);
        var items = writes
            .SelectMany(request => request.RequestItems["staging-table"])
            .Select(write => write.PutRequest.Item)
            .OrderBy(item => int.Parse(item["sequenceNumber"].N))
            .ToList();
        Assert.Equal(26, items.Count);
        Assert.Equal("1", items[0]["sequenceNumber"].N);
        Assert.Equal("26", items[^1]["sequenceNumber"].N);
        Assert.All(items, item =>
        {
            Assert.Equal("job-1", item["jobId"].S);
            Assert.NotEmpty(item["CreatedAtUtc"].S);
            Assert.True(long.Parse(item["ExpiresAt"].N) > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.NotEmpty(item["finding"].M);
        });
    }

    [Fact]
    public void DeleteByJobId_PaginatesAndDeletesAllProjectedKeys()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var queryCalls = 0;
        var deleteRequests = new List<WriteRequest>();
        client
            .Setup(db => db.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((QueryRequest request, CancellationToken _) =>
            {
                queryCalls++;
                if (queryCalls == 1)
                {
                    return Task.FromResult(new QueryResponse
                    {
                        Items = new List<Dictionary<string, AttributeValue>>
                        {
                            Key("job-2", 1),
                            Key("job-2", 2)
                        },
                        LastEvaluatedKey = Key("job-2", 2)
                    });
                }

                Assert.NotNull(request.ExclusiveStartKey);
                return Task.FromResult(new QueryResponse
                {
                    Items = new List<Dictionary<string, AttributeValue>>
                    {
                        Key("job-2", 3)
                    },
                    LastEvaluatedKey = new Dictionary<string, AttributeValue>()
                });
            });
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BatchWriteItemRequest, CancellationToken>((request, _) =>
                deleteRequests.AddRange(request.RequestItems["staging-table"]))
            .ReturnsAsync(Success());
        var repository = CreateRepository(client, concurrency: 2);

        repository.DeleteByJobId("job-2");

        Assert.Equal(2, queryCalls);
        Assert.Equal(3, deleteRequests.Count);
        Assert.Equal(
            new[] { "1", "2", "3" },
            deleteRequests
                .Select(write => write.DeleteRequest.Key["sequenceNumber"].N)
                .OrderBy(value => int.Parse(value)));
    }

    [Fact]
    public void SaveValidFindings_RetriesOnlyUnprocessedItems()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var batchCalls = new List<BatchWriteItemRequest>();
        var invocation = 0;
        client
            .Setup(db => db.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyQuery());
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns((BatchWriteItemRequest request, CancellationToken _) =>
            {
                batchCalls.Add(request);
                invocation++;
                if (invocation == 1)
                {
                    return Task.FromResult(new BatchWriteItemResponse
                    {
                        UnprocessedItems = new Dictionary<string, List<WriteRequest>>
                        {
                            ["staging-table"] = request.RequestItems["staging-table"]
                                .Take(1)
                                .ToList()
                        }
                    });
                }

                return Task.FromResult(Success());
            });
        var repository = CreateRepository(client, concurrency: 1);

        repository.SaveValidFindings(
            "job-retry",
            new List<FileFinding>
            {
                Finding("one.txt"),
                Finding("two.txt")
            });

        Assert.Equal(2, batchCalls.Count);
        Assert.Equal(2, batchCalls[0].RequestItems["staging-table"].Count);
        Assert.Single(batchCalls[1].RequestItems["staging-table"]);
    }

    [Fact]
    public void InvalidAndEmptyInputs_DoNotWrite()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var repository = CreateRepository(client, concurrency: 4);

        Assert.Throws<ArgumentException>(() =>
            repository.SaveValidFindings(" ", new List<FileFinding> { Finding("one.txt") }));
        repository.SaveValidFindings("job", null!);
        repository.SaveValidFindings("job", new List<FileFinding>());
        repository.DeleteByJobId(" ");

        client.VerifyNoOtherCalls();
    }

    [Fact]
    public void SaveValidFindings_BatchFailure_IsWrappedWithRecoveryContext()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.QueryAsync(
                It.IsAny<QueryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyQuery());
        client
            .Setup(db => db.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonDynamoDBException("write failed"));
        var repository = CreateRepository(client, concurrency: 1);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            repository.SaveValidFindings(
                "job-failure",
                new List<FileFinding> { Finding("one.txt") }));

        Assert.Contains("Staging save failed", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    private static DynamoDbIngestionStagingRepository CreateRepository(
        Mock<IAmazonDynamoDB> client,
        int concurrency)
        => new(
            client.Object,
            Options.Create(new DynamoDbOptions
            {
                StagedFindingsTableName = "staging-table",
                MaxBatchWriteConcurrency = concurrency
            }),
            NullLogger<DynamoDbIngestionStagingRepository>.Instance);

    private static QueryResponse EmptyQuery()
        => new()
        {
            Items = new List<Dictionary<string, AttributeValue>>(),
            LastEvaluatedKey = new Dictionary<string, AttributeValue>()
        };

    private static BatchWriteItemResponse Success()
        => new()
        {
            UnprocessedItems = new Dictionary<string, List<WriteRequest>>()
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

    private static Dictionary<string, AttributeValue> Key(
        string jobId,
        int sequenceNumber)
        => new()
        {
            ["jobId"] = new AttributeValue { S = jobId },
            ["sequenceNumber"] = new AttributeValue { N = sequenceNumber.ToString() }
        };
}
