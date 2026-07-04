using Amazon.DynamoDBv2;

using Amazon.DynamoDBv2.Model;

using Microsoft.Extensions.Options;

using RemediationTool.Application.Repositories;

using RemediationTool.Domain.Entities;

using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>

/// DynamoDB implementation of IRejectedRowRepository.

/// Table: gfr-edg-rejected-dev

/// Primary key: "id" (lowercase) — confirmed from live table sample.

/// GSI: uid-rowCreatedDateOn-index → query all rejections for a job.

/// Note: old GSI "jobId-errorDateUtc-index" no longer exists in new table.

/// </summary>

public class DynamoDbRejectedRowRepository : IRejectedRowRepository

{

    private readonly IAmazonDynamoDB _dynamoDb;

    private readonly string _tableName;

    private const int DynamoDbBatchLimit = 25;

    public DynamoDbRejectedRowRepository(

        IAmazonDynamoDB dynamoDb,

        IOptions<DynamoDbOptions> options)

    {

        _dynamoDb = dynamoDb;

        _tableName = options.Value.RejectedRowsTableName;

    }

    public List<RejectedRowDetail> GetAll()

    {

        var rows = new List<RejectedRowDetail>();

        Dictionary<string, AttributeValue>? lastKey = null;

        do

        {

            var response = _dynamoDb.ScanAsync(new ScanRequest

            {

                TableName = _tableName,

                ExclusiveStartKey = lastKey

            }).GetAwaiter().GetResult();

            rows.AddRange(response.Items.Select(DynamoDbAttributeMap.ToRejectedRowDetail));

            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;

        }

        while (lastKey != null);

        return rows;

    }

    public List<RejectedRowDetail> GetByJobId(string jobId)

    {

        if (string.IsNullOrWhiteSpace(jobId)) return new List<RejectedRowDetail>();

        var rows = new List<RejectedRowDetail>();

        Dictionary<string, AttributeValue>? lastKey = null;

        do

        {

            // NEW: query by "uid" using the uid-rowCreatedDateOn-index GSI.

            // "uid" in gfr-edg-rejected-dev = the job link (= reportUid).

            // Old table used "jobId" + "jobId-errorDateUtc-index" GSI — both renamed.

            var response = _dynamoDb.QueryAsync(new QueryRequest

            {

                TableName = _tableName,

                IndexName = "uid-rowCreatedDateOn-index",

                KeyConditionExpression = "#uid = :uid",

                ExpressionAttributeNames = new Dictionary<string, string> { ["#uid"] = "uid" },

                ExpressionAttributeValues = new Dictionary<string, AttributeValue>

                {

                    [":uid"] = new AttributeValue { S = jobId }

                },

                ExclusiveStartKey = lastKey

            }).GetAwaiter().GetResult();

            rows.AddRange(response.Items.Select(DynamoDbAttributeMap.ToRejectedRowDetail));

            lastKey = response.LastEvaluatedKey?.Count > 0 ? response.LastEvaluatedKey : null;

        }

        while (lastKey != null);

        return rows;

    }

    public void AddRange(List<RejectedRowDetail> rejectedRows)

    {

        if (rejectedRows == null || rejectedRows.Count == 0) return;

        foreach (var chunk in rejectedRows.Chunk(DynamoDbBatchLimit))

        {

            var requests = chunk.Select(r => new WriteRequest

            {

                PutRequest = new PutRequest { Item = DynamoDbAttributeMap.ToMap(r) }

            }).ToList();

            _dynamoDb.BatchWriteItemAsync(new BatchWriteItemRequest

            {

                RequestItems = new Dictionary<string, List<WriteRequest>>

                {

                    [_tableName] = requests

                }

            }).GetAwaiter().GetResult();

        }

    }

}
