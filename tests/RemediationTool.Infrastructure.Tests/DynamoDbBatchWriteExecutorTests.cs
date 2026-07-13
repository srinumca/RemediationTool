using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Moq;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class DynamoDbBatchWriteExecutorTests
{
    private const string TableName = "test-table";

    [Fact]
    public async Task WriteAsync_RetriesOnlyUnprocessedItemsUntilAllAreAccepted()
    {
        var requests = CreateRequests(3);
        var dynamoDb = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var capturedRequests = new List<BatchWriteItemRequest>();
        var callNumber = 0;

        dynamoDb
            .Setup(client => client.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BatchWriteItemRequest request, CancellationToken _) =>
            {
                capturedRequests.Add(request);
                callNumber++;

                return callNumber == 1
                    ? new BatchWriteItemResponse
                    {
                        UnprocessedItems = new Dictionary<string, List<WriteRequest>>
                        {
                            [TableName] = new List<WriteRequest> { requests[2] }
                        }
                    }
                    : new BatchWriteItemResponse
                    {
                        UnprocessedItems = new Dictionary<string, List<WriteRequest>>()
                    };
            });

        await DynamoDbBatchWriteExecutor.WriteAsync(
            dynamoDb.Object,
            TableName,
            requests,
            operationName: "TestWrite",
            batchNumber: 1,
            totalInputCount: requests.Count,
            maxUnprocessedItemRetryAttempts: 5,
            Mock.Of<ILogger>(),
            CancellationToken.None);

        Assert.Equal(2, capturedRequests.Count);
        Assert.Equal(3, capturedRequests[0].RequestItems[TableName].Count);
        Assert.Single(capturedRequests[1].RequestItems[TableName]);
        Assert.Same(requests[2], capturedRequests[1].RequestItems[TableName][0]);

        dynamoDb.Verify(
            client => client.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task WriteAsync_ThrowsWhenUnprocessedItemsRemainAfterRetryLimit()
    {
        var requests = CreateRequests(1);
        var dynamoDb = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);

        dynamoDb
            .Setup(client => client.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchWriteItemResponse
            {
                UnprocessedItems = new Dictionary<string, List<WriteRequest>>
                {
                    [TableName] = requests
                }
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DynamoDbBatchWriteExecutor.WriteAsync(
                dynamoDb.Object,
                TableName,
                requests,
                operationName: "TestWrite",
                batchNumber: 1,
                totalInputCount: requests.Count,
                maxUnprocessedItemRetryAttempts: 2,
                Mock.Of<ILogger>(),
                CancellationToken.None));

        Assert.Contains("remained unprocessed", exception.Message, StringComparison.OrdinalIgnoreCase);

        dynamoDb.Verify(
            client => client.BatchWriteItemAsync(
                It.IsAny<BatchWriteItemRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static List<WriteRequest> CreateRequests(int count)
    {
        var requests = new List<WriteRequest>(count);

        for (var index = 0; index < count; index++)
        {
            requests.Add(new WriteRequest
            {
                PutRequest = new PutRequest
                {
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["id"] = new AttributeValue { S = index.ToString() }
                    }
                }
            });
        }

        return requests;
    }
}
