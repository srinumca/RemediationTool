namespace RemediationTool.Application.Options;

/// <summary>
/// Configurable settings for the quarantine lifecycle.
/// Keeps business rules out of service code so they can be changed per environment.
/// </summary>
public sealed class QuarantineProcessingOptions
{
    public const string SectionName = "QuarantineProcessing";

    /// <summary>Minimum age of a file before it is eligible for quarantine.</summary>
    public int RetentionYears { get; set; } = 10;

    /// <summary>Maximum quarantine attempts before the file is moved to Error.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Delay between quarantine retry attempts.</summary>
    public int RetryDelayMilliseconds { get; set; } = 1000;

    /// <summary>Local/source root used by the local file service when inbound paths are relative.</summary>
    public string SourceRootPath { get; set; } = "storage/source";

    /// <summary>Quarantine root used by the local file service.</summary>
    public string QuarantineRootPath { get; set; } = "storage/quarantine";

    /// <summary>Suffix used when creating the placeholder/stub file.</summary>
    public string StubFileSuffix { get; set; } = "_Retention_Placeholder";

    /// <summary>Admin-configurable message written into the stub file.</summary>
    public string StubMessage { get; set; } =
        "This file was moved due to ADP retention policy. Contact admin to request restore.";

    /// <summary>Allows local/debug environments to bypass retention checks when needed.</summary>
    public bool EnableRetentionCheck { get; set; } = true;
}
