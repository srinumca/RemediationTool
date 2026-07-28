using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.DynamoDB;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// Reads canonical source-system definitions from DynamoDB.
/// </summary>
public sealed class DynamoDbSourceSystemRepository : ISourceSystemRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly ILogger<DynamoDbSourceSystemRepository> _logger;

    public DynamoDbSourceSystemRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<DynamoDbOptions> options,
        ILogger<DynamoDbSourceSystemRepository> logger)
    {
        _dynamoDb = dynamoDb;
        _tableName = options.Value.SourceSystemsTableName;
        _logger = logger;
    }

    public async Task<SourceSystemDefinition?> GetBySourceSystemAsync(
        string sourceSystem,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem))
            return null;

        var normalizedSourceSystem = sourceSystem.Trim();

        try
        {
            var response = await _dynamoDb.GetItemAsync(
                new GetItemRequest
                {
                    TableName = _tableName,
                    ConsistentRead = true,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["sourceSystem"] = new AttributeValue { S = normalizedSourceSystem }
                    }
                },
                cancellationToken);

            if (response.Item is null || response.Item.Count == 0)
            {
                _logger.LogWarning(
                    "Source system was not found. SourceSystem: {SourceSystem}",
                    normalizedSourceSystem);
                return null;
            }

            return new SourceSystemDefinition
            {
                SourceSystem = GetString(response.Item, "sourceSystem") ?? normalizedSourceSystem,
                CreatedAt = GetDate(response.Item, "createdAt"),
                DataSystem = GetString(response.Item, "dataSystem"),
                Description = GetString(response.Item, "description"),
                DisplayName = GetString(response.Item, "displayName"),
                IsEnabled = GetBoolean(response.Item, "isEnabled"),
                OriginatingDataSystem = GetString(response.Item, "originatingDataSystem"),
                UpdatedAt = GetDate(response.Item, "updatedAt")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to read source system configuration. SourceSystem: {SourceSystem}, Table: {TableName}",
                normalizedSourceSystem,
                _tableName);
            throw;
        }
    }

    private static string? GetString(
        IReadOnlyDictionary<string, AttributeValue> item,
        string attributeName)
        => item.TryGetValue(attributeName, out var value) ? value.S : null;

    private static bool GetBoolean(
        IReadOnlyDictionary<string, AttributeValue> item,
        string attributeName)
        => item.TryGetValue(attributeName, out var value) && value.BOOL == true;

    private static DateTime? GetDate(
        IReadOnlyDictionary<string, AttributeValue> item,
        string attributeName)
    {
        var rawValue = GetString(item, attributeName);
        return DateTime.TryParse(
            rawValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
