namespace RemediationTool.Infrastructure.DynamoDB;

public class DynamoDbOptions
{
    public const string SectionName = "AWS:DynamoDB";

    public string FindingsTableName { get; set; } = "RemediationFindings-dev";
    public string HistoryTableName { get; set; } = "FindingHistory-dev";
    public string JobAuditTableName { get; set; } = "IngestionJobAudit-dev";
    public string RejectedRowsTableName { get; set; } = "RejectedRows-dev";
    public string CheckpointsTableName { get; set; } = "IngestionCheckpoints-dev";
    public string StagedFindingsTableName { get; set; } = "IngestionStagedFindings-dev";
}