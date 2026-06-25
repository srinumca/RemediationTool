using FluentValidation;
using RemediationTool.Application.Constants;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Validators;

/// <summary>
/// Row-level validation rules applied to every FileFinding during ingestion.
/// A failing record is rejected and logged — it does NOT block other rows.
///
/// Rules are aligned with the Inbound File Layout specification.
/// Extended to support all GFR finding types including
/// TotalPendingQuarantined, Restoration, Exception, Error.
/// </summary>
public class FileFindingValidator : AbstractValidator<FileFinding>
{
    private static readonly string[] AllowedDataSystems =
    {
        "SMB", "NFS", "M365", "OneDrive", "SharePoint"
    };

    private static readonly string[] AllowedRiskLevels =
    {
        "Low", "Medium", "High", "Critical"
    };

    public FileFindingValidator()
    {
        // ---------------------------------------------------------------
        // Required string fields
        // ---------------------------------------------------------------

        RuleFor(x => x.FindingFileName)
            .NotEmpty()
            .WithMessage("Finding_File_Name is required.")
            .MaximumLength(512)
            .WithMessage("Finding_File_Name cannot exceed 512 characters.");

        RuleFor(x => x.FindingFileFormat)
            .NotEmpty()
            .WithMessage("Finding File Format is required.")
            .MaximumLength(50)
            .WithMessage("Finding File Format cannot exceed 50 characters.");

        RuleFor(x => x.CurrentFileLocation)
            .NotEmpty()
            .WithMessage("Current_File_Location is required.")
            .MaximumLength(4000)
            .WithMessage("Current_File_Location cannot exceed 4000 characters.");

        RuleFor(x => x.OriginatingDataSystem)
            .NotEmpty()
            .WithMessage("Originating_Data_System is required.")
            .Must(v => AllowedDataSystems.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Originating_Data_System must be one of: {string.Join(", ", AllowedDataSystems)}.");

        RuleFor(x => x.OriginatingVendorTool)
            .NotEmpty()
            .WithMessage("Originating_Vendor_Tool is required.")
            .MaximumLength(100)
            .WithMessage("Originating_Vendor_Tool cannot exceed 100 characters.");

        // ---------------------------------------------------------------
        // FindingType — validated against all allowed values
        // ---------------------------------------------------------------

        RuleFor(x => x.FindingType)
            .NotEmpty()
            .WithMessage("Finding_Type is required.")
            .Must(v => FindingTypes.AllAllowedTypes.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Finding_Type is invalid. Allowed values: {string.Join(", ", FindingTypes.AllAllowedTypes)}.");

        // ---------------------------------------------------------------
        // Conditional rules — Quarantined records
        // ---------------------------------------------------------------

        When(x => x.FindingType.Equals(FindingTypes.Quarantined, StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.OriginalFileLocation)
                .NotEmpty()
                .WithMessage("Original_File_Location is required when Finding_Type is Quarantined.");

            RuleFor(x => x.QuarantineDateUtc)
                .NotNull()
                .WithMessage("Quarantine_Date is required when Finding_Type is Quarantined.");
        });

        // ---------------------------------------------------------------
        // Optional numeric fields
        // ---------------------------------------------------------------

        RuleFor(x => x.FindingFileSizeBytes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.FindingFileSizeBytes.HasValue)
            .WithMessage("Finding_File_Size must be >= 0.");

        // ---------------------------------------------------------------
        // Optional date fields — cannot be in the future
        // ---------------------------------------------------------------

        RuleFor(x => x.LastModifiedDateUtc)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .When(x => x.LastModifiedDateUtc.HasValue)
            .WithMessage("Last_Modified_Date cannot be in the future.");

        RuleFor(x => x.CreatedDateUtc)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .When(x => x.CreatedDateUtc.HasValue)
            .WithMessage("Created_Date cannot be in the future.");

        RuleFor(x => x.LastAccessedDateUtc)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .When(x => x.LastAccessedDateUtc.HasValue)
            .WithMessage("Last_Accessed_Date cannot be in the future.");

        RuleFor(x => x.DetectionDateUtc)
            .LessThanOrEqualTo(DateTime.UtcNow.AddDays(1))
            .When(x => x.DetectionDateUtc.HasValue)
            .WithMessage("Detection_Date cannot be in the future.");

        // ---------------------------------------------------------------
        // Optional enum-like fields
        // ---------------------------------------------------------------

        RuleFor(x => x.RiskLevel)
            .Must(v => string.IsNullOrWhiteSpace(v) ||
                       AllowedRiskLevels.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Risk_Level must be one of: {string.Join(", ", AllowedRiskLevels)}.");

        // ---------------------------------------------------------------
        // Email format validation
        // ---------------------------------------------------------------

        RuleFor(x => x.RestorationRequestorEmail)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.RestorationRequestorEmail))
            .WithMessage("Restoration_Requestor_Email must be a valid email address.");
    }
}