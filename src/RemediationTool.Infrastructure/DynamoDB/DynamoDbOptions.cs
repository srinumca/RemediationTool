namespace RemediationTool.Infrastructure.DynamoDB;

public class DynamoDbOptions
{
    public const string SectionName = "AWS:DynamoDB";

    public string FindingsTableName { get; set; } = "RemediationFindings";
    public string HistoryTableName { get; set; } = "FindingHistory";
    public string JobAuditTableName { get; set; } = "IngestionJobAudit";
    public string RejectedRowsTableName { get; set; } = "RejectedRows";
    public string CheckpointsTableName { get; set; } = "IngestionCheckpoints";
    public string StagedFindingsTableName { get; set; } = "IngestionStagedFindings";
}