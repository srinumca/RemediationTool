using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RemediationTool.Infrastructure.DynamoDB;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class DynamoDbTableInitialiserTests
{
    public static TheoryData<TableCreationCase> TableCases => new()
    {
        new TableCreationCase(
            "findings-table",
            new[] { ("Id", KeyType.HASH) },
            new[]
            {
                "FindingType-LoadDateUtc-index",
                "DataSystem-LoadDateUtc-index",
                "IngestionJobId-LoadDateUtc-index",
                "SourceRecordId-LoadDateUtc-index"
            },
            TtlExpected: false),
        new TableCreationCase(
            "history-table",
            new[]
            {
                ("SourceRecordId", KeyType.HASH),
                ("ChangedAtUtc", KeyType.RANGE)
            },
            new[]
            {
                "FindingId-ChangedAtUtc-index",
                "IngestionJobId-ChangedAtUtc-index"
            },
            TtlExpected: false),
        new TableCreationCase(
            "reports-table",
            new[] { ("JobId", KeyType.HASH) },
            new[] { "Status-StartTimestampUtc-index" },
            TtlExpected: false),
        new TableCreationCase(
            "rejected-table",
            new[] { ("RejectedRowId", KeyType.HASH) },
            new[] { "JobId-ErrorDateUtc-index" },
            TtlExpected: false),
        new TableCreationCase(
            "checkpoints-table",
            new[] { ("JobId", KeyType.HASH) },
            Array.Empty<string>(),
            TtlExpected: false),
        new TableCreationCase(
            "staging-table",
            new[]
            {
                ("JobId", KeyType.HASH),
                ("SequenceNumber", KeyType.RANGE)
            },
            Array.Empty<string>(),
            TtlExpected: true)
    };

    [Fact]
    public async Task InitialiseAsync_AllTablesExist_DoesNotCreateOrConfigureTtl()
    {
        var client = new Mock<IAmazonDynamoDB>();
        var describedTables = new List<string>();
        client
            .Setup(db => db.DescribeTableAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((table, _) => describedTables.Add(table))
            .ReturnsAsync(ActiveTable("existing"));
        var initialiser = CreateInitialiser(client);

        await initialiser.InitialiseAsync();

        Assert.Equal(
            new[]
            {
                "findings-table",
                "history-table",
                "reports-table",
                "rejected-table",
                "checkpoints-table",
                "staging-table"
            },
            describedTables);
        client.Verify(
            db => db.CreateTableAsync(
                It.IsAny<CreateTableRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            db => db.UpdateTimeToLiveAsync(
                It.IsAny<UpdateTimeToLiveRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(TableCases))]
    public async Task InitialiseAsync_MissingTable_CreatesExpectedOnDemandSchema(
        TableCreationCase tableCase)
    {
        var client = new Mock<IAmazonDynamoDB>();
        var targetDescribeCount = 0;
        CreateTableRequest? created = null;
        UpdateTimeToLiveRequest? ttl = null;
        client
            .Setup(db => db.DescribeTableAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string tableName, CancellationToken _) =>
            {
                if (string.Equals(tableName, tableCase.TableName, StringComparison.Ordinal)
                    && Interlocked.Increment(ref targetDescribeCount) == 1)
                {
                    return Task.FromException<DescribeTableResponse>(
                        new ResourceNotFoundException("missing"));
                }

                return Task.FromResult(ActiveTable(tableName));
            });
        client
            .Setup(db => db.CreateTableAsync(
                It.IsAny<CreateTableRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateTableRequest, CancellationToken>((request, _) => created = request)
            .ReturnsAsync(new CreateTableResponse());
        client
            .Setup(db => db.UpdateTimeToLiveAsync(
                It.IsAny<UpdateTimeToLiveRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<UpdateTimeToLiveRequest, CancellationToken>((request, _) => ttl = request)
            .ReturnsAsync(new UpdateTimeToLiveResponse());
        var initialiser = CreateInitialiser(client);

        await initialiser.InitialiseAsync();

        Assert.NotNull(created);
        Assert.Equal(tableCase.TableName, created.TableName);
        Assert.Equal(BillingMode.PAY_PER_REQUEST, created.BillingMode);
        Assert.Equal(
            tableCase.Keys,
            created.KeySchema
                .Select(key => (key.AttributeName, key.KeyType))
                .ToArray());
        Assert.Equal(
            tableCase.IndexNames.OrderBy(name => name, StringComparer.Ordinal),
            (created.GlobalSecondaryIndexes ?? new List<GlobalSecondaryIndex>())
                .Select(index => index.IndexName)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(
            created.GlobalSecondaryIndexes ?? new List<GlobalSecondaryIndex>(),
            index => Assert.Equal(ProjectionType.ALL, index.Projection.ProjectionType));

        if (tableCase.TtlExpected)
        {
            Assert.NotNull(ttl);
            Assert.Equal(tableCase.TableName, ttl.TableName);
            Assert.Equal("ExpiresAt", ttl.TimeToLiveSpecification.AttributeName);
            Assert.True(ttl.TimeToLiveSpecification.Enabled);
        }
        else
        {
            Assert.Null(ttl);
        }

        client.Verify(
            db => db.CreateTableAsync(
                It.Is<CreateTableRequest>(request => request.TableName == tableCase.TableName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InitialiseAsync_PreCancelledToken_PropagatesCancellation()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client
            .Setup(db => db.DescribeTableAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ActiveTable("unused"));
            });
        var initialiser = CreateInitialiser(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            initialiser.InitialiseAsync(cancellation.Token));

        client.Verify(
            db => db.CreateTableAsync(
                It.IsAny<CreateTableRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static DynamoDbTableInitialiser CreateInitialiser(
        Mock<IAmazonDynamoDB> client)
        => new(
            client.Object,
            Options.Create(new DynamoDbOptions
            {
                FindingsTableName = "findings-table",
                HistoryTableName = "history-table",
                JobAuditTableName = "reports-table",
                RejectedRowsTableName = "rejected-table",
                CheckpointsTableName = "checkpoints-table",
                StagedFindingsTableName = "staging-table"
            }),
            NullLogger<DynamoDbTableInitialiser>.Instance);

    private static DescribeTableResponse ActiveTable(string tableName)
        => new()
        {
            Table = new TableDescription
            {
                TableName = tableName,
                TableStatus = TableStatus.ACTIVE
            }
        };

    public sealed record TableCreationCase(
        string TableName,
        IReadOnlyList<(string AttributeName, KeyType KeyType)> Keys,
        IReadOnlyList<string> IndexNames,
        bool TtlExpected);
}
