using System.Text.Json.Serialization;

namespace RemediationTool.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of a file finding, as defined in the Data Model specification.
/// These values are stored as their string representation (e.g. "Obsolete") for human-readable
/// persistence in JSON and DynamoDB, and for direct mapping from inbound CSV/XLSX files.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FindingType
{
    /// <summary>
    /// File has been identified as exceeding the retention threshold (10+ years).
    /// This is the initial state set by the source scanning tool (e.g. Securiti.ai).
    /// </summary>
    Obsolete,

    /// <summary>
    /// File has been moved to the centralised quarantine location.
    /// A breadcrumb/stub file has been placed at the original path.
    /// </summary>
    Quarantined,

    /// <summary>
    /// File has been restored from quarantine back to its original location.
    /// The breadcrumb file has been removed.
    /// </summary>
    Restored,

    /// <summary>
    /// File has been permanently and securely deleted (hard delete) after the
    /// quarantine hold period expired. This action is irreversible.
    /// </summary>
    Deleted,

    /// <summary>
    /// File was evaluated for quarantine but found to no longer meet the obsolete
    /// criteria at the time of processing (e.g. modified within the last year).
    /// No action was taken.
    /// </summary>
    NotObsolete,

    /// <summary>
    /// File has been explicitly excluded from further remediation actions.
    /// Used for business continuity exceptions raised via the UI.
    /// The ExceptionDateUtc field is stamped when this state is set.
    /// </summary>
    Exclusion
}