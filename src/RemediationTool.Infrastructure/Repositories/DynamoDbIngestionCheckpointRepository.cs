using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// DynamoDB implementation used to persist ingestion checkpoints.
/// </summary>
public class DynamoDbIngestionCheckpointRepository : IIngestionCheckpointRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    public DynamoDbIngestionCheckpointRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.CheckpointsTableName;
    }

    public void Upsert(IngestionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        checkpoint.LastCheckpointUtc = DateTime.UtcNow;

        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(checkpoint)
        }).GetAwaiter().GetResult();
    }
}
