namespace RemediationTool.Infrastructure.DynamoDB;

/// <summary>
/// DynamoDB table and ingestion-write configuration.
/// Bound from appsettings.json section: AWS:DynamoDB.
/// </summary>
public class DynamoDbOptions
{
    public const string SectionName = "AWS:DynamoDB";

    public string FindingsTableName { get; set; } = "gfr-edg-findings-dev";
    public string JobAuditTableName { get; set; } = "gfr-edg-reports-dev";
    public string RejectedRowsTableName { get; set; } = "gfr-edg-rejected-dev";
    public string CheckpointsTableName { get; set; } = "gfr-ingestion-checkpoints-dev";
    public string StagedFindingsTableName { get; set; } = "gfr-ingestion-staged-findings-dev";
    public string HistoryTableName { get; set; } = "gfr-finding-history-dev";

    /// <summary>
    /// Maximum number of independent 25-item BatchWriteItem requests executed at once.
    /// A bounded value improves large-file throughput while preserving per-request retry
    /// handling and preventing uncontrolled DynamoDB throttling.
    /// </summary>
    public int MaxBatchWriteConcurrency { get; set; } = 4;
}
