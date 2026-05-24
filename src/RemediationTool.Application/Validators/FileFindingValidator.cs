using FluentValidation;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Validators;

public class FileFindingValidator : AbstractValidator<FileFinding>
{
    private static readonly string[] AllowedFindingTypes =
    {
        "Obsolete",
        "Quarantined",
        "Restored",
        "Deleted",
        "Not Obsolete",
        "Exclusion"
    };

    private static readonly string[] AllowedDataSystems =
    {
        "SMB",
        "NFS",
        "M365",
        "OneDrive",
        "SharePoint"
    };

    public FileFindingValidator()
    {
        RuleFor(x => x.FindingFileName)
            .NotEmpty()
            .WithMessage("Finding_File_Name is required");

        RuleFor(x => x.FindingFileFormat)
            .NotEmpty()
            .WithMessage("Finding File Format is required");

        RuleFor(x => x.CurrentFileLocation)
            .NotEmpty()
            .WithMessage("Current_File_Location is required");

        RuleFor(x => x.FindingType)
            .NotEmpty()
            .WithMessage("Finding_Type is required")
            .Must(value => AllowedFindingTypes.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Finding_Type is invalid");

        RuleFor(x => x.OriginatingDataSystem)
            .NotEmpty()
            .WithMessage("Originating_Data_System is required")
            .Must(value => AllowedDataSystems.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Originating_Data_System is invalid");

        RuleFor(x => x.OriginatingVendorTool)
            .NotEmpty()
            .WithMessage("Originating_Vendor_Tool is required");

        When(x => x.FindingType.Equals("Quarantined", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.OriginalFileLocation)
                .NotEmpty()
                .WithMessage("Original_File_Location is required when Finding_Type is Quarantined");

            RuleFor(x => x.QuarantineDate)
                .NotNull()
                .WithMessage("Quarantine_Date is required when Finding_Type is Quarantined");
        });
    }
}