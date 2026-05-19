namespace RemediationTool.Domain;

public enum FileStatus
{
    Loaded,
    NotEligible,
    Missing,
    Quarantined,
    Failed,
    Restored,
    Deleted
}