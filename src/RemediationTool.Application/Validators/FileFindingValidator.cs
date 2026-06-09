using FluentValidation;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Validators;

/// <summary>
/// FluentValidation validator for <see cref="FileFinding"/> records during ingestion.
///
/// Enforces all rules defined in the Inbound File Layout tab of the requirements specification.
/// The FindingType enum replaces the previous string-array allowlist — invalid enum values
/// are rejected at parse time during CSV/XLSX mapping before this validator even runs.
///
/// Validation is row-level: a failing record is rejected and logged, but does not
/// block processing of other records in the same ingestion job.
/// </summary>
public sealed class FileFindingValidator : AbstractValidator<FileFinding>
{
    private static readonly string[] AllowedDataSystems =
    {
        "SMB", "NFS", "M365", "OneDrive", "SharePoint"
    };

    public FileFindingValidator()
    {
        // ---------------------------------------------------------------
        // Mandatory string fields — reject if null/empty
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

        // DataSystem is mandatory per the Inbound File Layout spec.
        // Identifies the specific NetApp drive path (more granular than OriginatingDataSystem).
        RuleFor(x => x.DataSystem)
            .NotEmpty()
            .WithMessage("Data_System is required.")
            .MaximumLength(512)
            .WithMessage("Data_System cannot exceed 512 characters.");

        // ---------------------------------------------------------------
        // FindingType — validated at enum level (invalid values rejected at parse time)
        // Extra guard: FindingType must be a defined enum member
        // ---------------------------------------------------------------

        RuleFor(x => x.FindingType)
        .NotEmpty()
        .WithMessage("Finding_Type is required.")
        .Must(value => Enum.TryParse<FindingType>(
            value?.Replace(" ", ""),
            ignoreCase: true,
            out _))
        .WithMessage($"Finding_Type is invalid. Allowed values: {string.Join(", ", Enum.GetNames<FindingType>())}");
        // ---------------------------------------------------------------
        // Conditional rules for Quarantined records
        // ---------------------------------------------------------------

        When(x => x.FindingType == FindingType.Quarantined.ToString(), () =>
        {
            RuleFor(x => x.OriginalFileLocation)
                .NotEmpty()
                .WithMessage("Original_File_Location is required when Finding_Type is Quarantined.");

            RuleFor(x => x.QuarantineDateUtc)
                .NotNull()
                .WithMessage("Quarantine_Date is required when Finding_Type is Quarantined.");
        });

        // ---------------------------------------------------------------
        // Conditional rules for Exclusion records
        // ---------------------------------------------------------------

        When(x => x.FindingType == FindingType.Exclusion.ToString(), () =>
        {
            RuleFor(x => x.ExceptionDateUtc)
                .NotNull()
                .WithMessage("Exception_Date is required when Finding_Type is Exclusion.");
        });

        // ---------------------------------------------------------------
        // Optional numeric fields — range checks when present
        // ---------------------------------------------------------------

        RuleFor(x => x.FindingFileSizeBytes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.FindingFileSizeBytes.HasValue)
            .WithMessage("Finding_File_Size must be greater than or equal to 0.");

        // ---------------------------------------------------------------
        // Restoration fields — format validation when present
        // ---------------------------------------------------------------

        RuleFor(x => x.RestorationRequestorEmail)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.RestorationRequestorEmail))
            .WithMessage("Restoration_Requestor_Email must be a valid email address.");
    }
}