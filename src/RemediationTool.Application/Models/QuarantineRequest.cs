namespace RemediationTool.Application.Models;

/// <summary>
/// Request used by the UI/API to queue records for quarantine.
/// Supports selected IDs and select-all eligible behavior.
/// </summary>
public sealed class QuarantineRequest
{
    public List<Guid> RecordIds { get; set; } = new();

    /// <summary>
    /// When true, all eligible NotYetStarted obsolete records are queued.
    /// Used for select-all-across-pages behavior.
    /// </summary>
    public bool IncludeAllEligibleNotYetStarted { get; set; }

    /// <summary>Actor/user that initiated the quarantine action.</summary>
    public string RequestedBy { get; set; } = "System";

    /// <summary>
    /// When true, queued records are processed immediately after status changes to PendingQuarantine.
    /// When false, only queues records; the background/run endpoint processes later.
    /// </summary>
    public bool ProcessImmediately { get; set; } = true;
}
