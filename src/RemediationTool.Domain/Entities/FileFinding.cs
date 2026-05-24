using RemediationTool.Domain;

namespace RemediationTool.Domain.Entities;

public class FileFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string RecordVersionId { get; set; } = Guid.NewGuid().ToString("N");

    public string? SourceRecordId { get; set; }

    public string? IngestionJobId { get; set; }

    public string InboundFileName { get; set; } = string.Empty;

    public string UserName { get; set; } = "System";

    public DateTime LoadDateUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdateDateUtc { get; set; } = DateTime.UtcNow;

    public string FindingFileName { get; set; } = string.Empty;

    public string FindingFileFormat { get; set; } = string.Empty;

    public long? FindingFileSizeBytes { get; set; }

    public string CurrentFileLocation { get; set; } = string.Empty;

    public string FindingType { get; set; } = string.Empty;

    public string OriginatingDataSystem { get; set; } = string.Empty;

    public string OriginatingVendorTool { get; set; } = string.Empty;

    public DateTime? LastModifiedDateUtc { get; set; }

    public DateTime? CreatedDateUtc { get; set; }

    public DateTime? LastAccessedDateUtc { get; set; }

    public string? SiteOwner { get; set; }

    public string? FileOwner { get; set; }

    public string? BusinessUnit { get; set; }

    public string? Division { get; set; }

    public string? Department { get; set; }

    public string? Region { get; set; }

    public string? Country { get; set; }

    public string? PolicyName { get; set; }

    public string? PolicyId { get; set; }

    public string? FindingReason { get; set; }

    public string? RiskLevel { get; set; }

    public string? SensitivityLabel { get; set; }

    public DateTime? DetectionDateUtc { get; set; }

    public string? RecommendedAction { get; set; }

    public string? OriginalFileLocation { get; set; }

    public DateTime? QuarantineDateUtc { get; set; }

    public DateTime? RestoredDateUtc { get; set; }

    public DateTime? DeletedDateUtc { get; set; }

    public string? RestorationTicketIdentifier { get; set; }

    public string? RestorationRequestorEmail { get; set; }

    public string? RestorationComment { get; set; }

    public FileStatus Status { get; set; } = FileStatus.Loaded;

    public bool IsValid { get; set; } = true;

    public string ErrorReason { get; set; } = string.Empty;

    // Compatibility properties for existing POC code

    public string FileName
    {
        get => FindingFileName;
        set => FindingFileName = value ?? string.Empty;
    }

    public string FilePath
    {
        get => CurrentFileLocation;
        set => CurrentFileLocation = value ?? string.Empty;
    }

    public string SourceSystem
    {
        get => OriginatingDataSystem;
        set => OriginatingDataSystem = value ?? string.Empty;
    }

    public long FileSize
    {
        get => FindingFileSizeBytes ?? 0;
        set => FindingFileSizeBytes = value;
    }

    public string? QuarantinePath
    {
        get => Status == FileStatus.Quarantined ? CurrentFileLocation : null;
        set => CurrentFileLocation = value ?? string.Empty;
    }

    public DateTime LastModifiedDate
    {
        get => LastModifiedDateUtc ?? DateTime.MinValue;
        set => LastModifiedDateUtc = value;
    }

    public string? IngestionId
    {
        get => IngestionJobId;
        set => IngestionJobId = value;
    }

    public string UploadedBy
    {
        get => UserName;
        set => UserName = value ?? "System";
    }

    public DateTime LoadDate
    {
        get => LoadDateUtc;
        set => LoadDateUtc = value;
    }

    public DateTime UpdatedDate
    {
        get => LastUpdateDateUtc;
        set => LastUpdateDateUtc = value;
    }

    public DateTime? QuarantineDate
    {
        get => QuarantineDateUtc;
        set => QuarantineDateUtc = value;
    }
}