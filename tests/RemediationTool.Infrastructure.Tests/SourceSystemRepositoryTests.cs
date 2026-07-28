using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Infrastructure.DynamoDB;
using RemediationTool.Infrastructure.Repositories;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class SourceSystemRepositoryTests
{
    [Fact]
    public async Task GetBySourceSystemAsync_BlankValue_ReturnsNullWithoutAwsCall()
    {
        var client = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var repository = CreateRepository(client);

        var result = await repository.GetBySourceSystemAsync(" ");

        Assert.Null(result);
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetBySourceSystemAsync_UsesConfiguredTableAndMapsItem()
    {
        var client = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        GetItemRequest? capturedRequest = null;
        client
            .Setup(db => db.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<GetItemRequest, CancellationToken>(
                (request, _) => capturedRequest = request)
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["sourceSystem"] = new() { S = "NetApp" },
                    ["createdAt"] = new() { S = "2026-07-28T06:41:57.8727778Z" },
                    ["dataSystem"] = new() { S = "NetApp" },
                    ["description"] = new() { S = "On-premises NetApp filer integration" },
                    ["displayName"] = new() { S = "NetApp File Server" },
                    ["isEnabled"] = new() { BOOL = true },
                    ["originatingDataSystem"] = new() { S = "smb" },
                    ["updatedAt"] = new() { S = "2026-07-28T06:41:57.8727778Z" }
                }
            });
        var repository = CreateRepository(client);

        var result = await repository.GetBySourceSystemAsync(" NetApp ");

        Assert.NotNull(result);
        Assert.Equal("source-systems-table", capturedRequest?.TableName);
        Assert.True(capturedRequest?.ConsistentRead);
        Assert.Equal("NetApp", capturedRequest?.Key["sourceSystem"].S);
        Assert.Equal("NetApp", result.SourceSystem);
        Assert.Equal("NetApp", result.DataSystem);
        Assert.Equal("NetApp File Server", result.DisplayName);
        Assert.True(result.IsEnabled);
        Assert.Equal("smb", result.OriginatingDataSystem);
        Assert.NotNull(result.CreatedAt);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task GetBySourceSystemAsync_MissingItem_ReturnsNull()
    {
        var client = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        client
            .Setup(db => db.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>()
            });
        var repository = CreateRepository(client);

        var result = await repository.GetBySourceSystemAsync("Unknown");

        Assert.Null(result);
    }

    private static DynamoDbSourceSystemRepository CreateRepository(
        Mock<IAmazonDynamoDB> client)
        => new(
            client.Object,
            Options.Create(new DynamoDbOptions
            {
                SourceSystemsTableName = "source-systems-table"
            }),
            NullLogger<DynamoDbSourceSystemRepository>.Instance);
}
