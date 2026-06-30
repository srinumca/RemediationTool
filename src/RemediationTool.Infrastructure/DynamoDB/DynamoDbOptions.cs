namespace RemediationTool.Infrastructure.DynamoDB;

/// <summary>
/// DynamoDB table name configuration.
/// Bound from appsettings.json section: AWS:DynamoDB
/// Note: section key must be exactly "DynamoDB" (case-sensitive).
///
/// Tables in use (5 — finding history table removed):
///   gfr-edg-reports-dev             → job audit / report records
///   gfr-edg-findings-dev            → valid ingested findings
///   gfr-edg-rejected-dev            → validation failures
///   gfr-ingestion-checkpoints-dev   → resume checkpoints
///   gfr-ingestion-staged-findings-dev → temp staging (TTL 7 days)
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

    // Removed: HistoryTableName — gfr-finding-history-dev not used
}