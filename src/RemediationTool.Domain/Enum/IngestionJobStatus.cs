namespace RemediationTool.Domain.Enum;

public enum IngestionJobStatus
{
    Started       = 1,
    Success       = 2,
    PartialSuccess = 3,
    Failed        = 4,
    Completed     = 5   // added to match gfr-edg-reports-dev sample (status = "Completed")
}