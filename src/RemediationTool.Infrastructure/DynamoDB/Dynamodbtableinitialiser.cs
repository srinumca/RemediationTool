using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RemediationTool.Infrastructure.DynamoDB;

/// <summary>
/// Runs once at application startup (when Persistence:Provider = DynamoDB).
/// Checks whether each required DynamoDB table exists and creates it if not.
///
/// This means a developer only needs to:
///   1. Configure AWS credentials (aws configure)
///   2. Set "Persistence:Provider": "DynamoDB" in appsettings
///   3. Run the application
///   → All 6 tables are created automatically. No manual AWS Console steps.
///
/// In production, CDK has already created the tables before the app starts,
/// so this class finds them all existing and does nothing.
/// </summary>
public class DynamoDbTableInitialiser
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly DynamoDbOptions _options;
    private readonly ILogger<DynamoDbTableInitialiser> _logger;

    public DynamoDbTableInitialiser(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbTableInitialiser> logger)
    {
        _dynamoDb = dynamoDb;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DynamoDB table initialisation starting...");

        await EnsureRemediationFindingsAsync(cancellationToken);
        await EnsureFindingHistoryAsync(cancellationToken);
        await EnsureIngestionJobAuditAsync(cancellationToken);
        await EnsureRejectedRowsAsync(cancellationToken);
        await EnsureIngestionCheckpointsAsync(cancellationToken);
        await EnsureIngestionStagedFindingsAsync(cancellationToken);

        _logger.LogInformation("DynamoDB table initialisation complete.");
    }

    // -------------------------------------------------------------------------
    // TABLE 1 — RemediationFindings
    // -------------------------------------------------------------------------
    private async Task EnsureRemediationFindingsAsync(CancellationToken ct)
    {
        var tableName = _options.FindingsTableName;

        if (await TableExistsAsync(tableName, ct)) return;

        _logger.LogInformation("Creating table {TableName}...", tableName);

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,

            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "Id",             AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "FindingType",    AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "DataSystem",     AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "IngestionJobId", AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "SourceRecordId", AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "LoadDateUtc",    AttributeType = ScalarAttributeType.S }
            },

            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "Id", KeyType = KeyType.HASH }
            },

            GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
            {
                Gsi("FindingType-LoadDateUtc-index",    "FindingType",    "LoadDateUtc"),
                Gsi("DataSystem-LoadDateUtc-index",     "DataSystem",     "LoadDateUtc"),
                Gsi("IngestionJobId-LoadDateUtc-index", "IngestionJobId", "LoadDateUtc"),
                Gsi("SourceRecordId-LoadDateUtc-index", "SourceRecordId", "LoadDateUtc")
            }
        }, ct);

        await WaitForTableActiveAsync(tableName, ct);
    }

    // -------------------------------------------------------------------------
    // TABLE 2 — FindingHistory
    // -------------------------------------------------------------------------
    private async Task EnsureFindingHistoryAsync(CancellationToken ct)
    {
        var tableName = _options.HistoryTableName;

        if (await TableExistsAsync(tableName, ct)) return;

        _logger.LogInformation("Creating table {TableName}...", tableName);

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,

            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "SourceRecordId", AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "ChangedAtUtc",   AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "FindingId",      AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "IngestionJobId", AttributeType = ScalarAttributeType.S }
            },

            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "SourceRecordId", KeyType = KeyType.HASH  },
                new() { AttributeName = "ChangedAtUtc",   KeyType = KeyType.RANGE }
            },

            GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
            {
                Gsi("FindingId-ChangedAtUtc-index",      "FindingId",      "ChangedAtUtc"),
                Gsi("IngestionJobId-ChangedAtUtc-index", "IngestionJobId", "ChangedAtUtc")
            }
        }, ct);

        await WaitForTableActiveAsync(tableName, ct);
    }

    // -------------------------------------------------------------------------
    // TABLE 3 — IngestionJobAudit
    // -------------------------------------------------------------------------
    private async Task EnsureIngestionJobAuditAsync(CancellationToken ct)
    {
        var tableName = _options.JobAuditTableName;

        if (await TableExistsAsync(tableName, ct)) return;

        _logger.LogInformation("Creating table {TableName}...", tableName);

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,

            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "JobId",              AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "Status",             AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "StartTimestampUtc",  AttributeType = ScalarAttributeType.S }
            },

            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "JobId", KeyType = KeyType.HASH }
            },

            GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
            {
                Gsi("Status-StartTimestampUtc-index", "Status", "StartTimestampUtc")
            }
        }, ct);

        await WaitForTableActiveAsync(tableName, ct);
    }

    // -------------------------------------------------------------------------
    // TABLE 4 — RejectedRows
    // -------------------------------------------------------------------------
    private async Task EnsureRejectedRowsAsync(CancellationToken ct)
    {
        var tableName = _options.RejectedRowsTableName;

        if (await TableExistsAsync(tableName, ct)) return;

        _logger.LogInformation("Creating table {TableName}...", tableName);

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,

            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "RejectedRowId", AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "JobId",         AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "ErrorDateUtc",  AttributeType = ScalarAttributeType.S }
            },

            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "RejectedRowId", KeyType = KeyType.HASH }
            },

            GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
            {
                Gsi("JobId-ErrorDateUtc-index", "JobId", "ErrorDateUtc")
            }
        }, ct);

        await WaitForTableActiveAsync(tableName, ct);
    }

    // -------------------------------------------------------------------------
    // TABLE 5 — IngestionCheckpoints
    // -------------------------------------------------------------------------
    private async Task EnsureIngestionCheckpointsAsync(CancellationToken ct)
    {
        var tableName = _options.CheckpointsTableName;

        if (await TableExistsAsync(tableName, ct)) return;

        _logger.LogInformation("Creating table {TableName}...", tableName);

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,

            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "JobId", AttributeType = ScalarAttributeType.S }
            },

            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "JobId", KeyType = KeyType.HASH }
            }
        }, ct);

        await WaitForTableActiveAsync(tableName, ct);
    }

    // -------------------------------------------------------------------------
    // TABLE 6 — IngestionStagedFindings  (TTL enabled on ExpiresAt)
    // -------------------------------------------------------------------------
    private async Task EnsureIngestionStagedFindingsAsync(CancellationToken ct)
    {
        var tableName = _options.StagedFindingsTableName;

        if (await TableExistsAsync(tableName, ct)) return;

        _logger.LogInformation("Creating table {TableName}...", tableName);

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,

            AttributeDefinitions = new List<AttributeDefinition>
            {
                new() { AttributeName = "JobId",          AttributeType = ScalarAttributeType.S },
                new() { AttributeName = "SequenceNumber", AttributeType = ScalarAttributeType.N }
            },

            KeySchema = new List<KeySchemaElement>
            {
                new() { AttributeName = "JobId",          KeyType = KeyType.HASH  },
                new() { AttributeName = "SequenceNumber", KeyType = KeyType.RANGE }
            }
        }, ct);

        await WaitForTableActiveAsync(tableName, ct);

        // Enable TTL on ExpiresAt — items auto-deleted after 7 days
        await _dynamoDb.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
        {
            TableName = tableName,
            TimeToLiveSpecification = new TimeToLiveSpecification
            {
                AttributeName = "ExpiresAt",
                Enabled = true
            }
        }, ct);

        _logger.LogInformation("TTL enabled on {TableName}.ExpiresAt", tableName);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        try
        {
            await _dynamoDb.DescribeTableAsync(tableName, ct);
            _logger.LogDebug("Table {TableName} already exists — skipping creation.", tableName);
            return true;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
    }

    private async Task WaitForTableActiveAsync(string tableName, CancellationToken ct)
    {
        // DynamoDB takes 10-30 seconds to create a table.
        // Poll until status = ACTIVE before continuing.
        var maxWait = TimeSpan.FromMinutes(3);
        var pollInterval = TimeSpan.FromSeconds(3);
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow - started < maxWait)
        {
            var response = await _dynamoDb.DescribeTableAsync(tableName, ct);
            var status = response.Table.TableStatus;

            if (status == TableStatus.ACTIVE)
            {
                _logger.LogInformation("Table {TableName} is ACTIVE.", tableName);
                return;
            }

            _logger.LogDebug(
                "Waiting for table {TableName} to become ACTIVE (current: {Status})...",
                tableName, status);

            await Task.Delay(pollInterval, ct);
        }

        throw new TimeoutException(
            $"Table '{tableName}' did not become ACTIVE within {maxWait.TotalMinutes} minutes.");
    }

    /// <summary>Convenience builder for a GSI with ALL projection and on-demand billing.</summary>
    private static GlobalSecondaryIndex Gsi(
        string indexName,
        string partitionKey,
        string sortKey) => new()
        {
            IndexName = indexName,
            KeySchema = new List<KeySchemaElement>
        {
            new() { AttributeName = partitionKey, KeyType = KeyType.HASH  },
            new() { AttributeName = sortKey,      KeyType = KeyType.RANGE }
        },
            Projection = new Projection { ProjectionType = ProjectionType.ALL }
        };
}