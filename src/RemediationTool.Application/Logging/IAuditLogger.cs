// ─────────────────────────────────────────────────────────────────────────────
// FILE: src/RemediationTool.Application/Logging/IAuditLogger.cs  (NEW FILE)
//
// PURPOSE: Distinguishes business/compliance-significant events (file
// quarantined, file deleted, file restored, ingestion job completed) from
// technical operational logs (DynamoDB writes, S3 calls, retries). Anything
// logged through this interface is tagged LogCategory=Audit and routed by
// Serilog to a SEPARATE file — logs/audit-YYYYMMDD.log — with its own
// retention policy, kept apart from the high-volume operational log.
// ─────────────────────────────────────────────────────────────────────────────

namespace RemediationTool.Application.Logging;

/// <summary>
/// Records business/compliance-significant events distinct from technical
/// operational logs. Used for anything that might be referenced in an audit:
/// file quarantined, file deleted, file restored, ingestion job completed,
/// rows rejected for validation reasons.
/// </summary>
public interface IAuditLogger
{
    void RecordEvent(
        string eventType,
        string entityId,
        string actor,
        string outcome,
        object? details = null);
}