using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

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

    public IngestionCheckpoint? GetByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;

        var response = _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["jobId"] = new AttributeValue { S = jobId }
            }
        }).GetAwaiter().GetResult();

        return response.Item?.Count > 0
            ? DynamoDbAttributeMap.ToIngestionCheckpoint(response.Item)
            : null;
    }

    public void Upsert(IngestionCheckpoint checkpoint)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));

        checkpoint.LastCheckpointUtc = DateTime.UtcNow;

        _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = DynamoDbAttributeMap.ToMap(checkpoint)
        }).GetAwaiter().GetResult();
    }
}