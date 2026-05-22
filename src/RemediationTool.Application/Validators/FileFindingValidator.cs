using FluentValidation;
using RemediationTool.Domain.Entities;

public class FileFindingValidator : AbstractValidator<FileFinding>
{
    public FileFindingValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithMessage("FileName is required");

        RuleFor(x => x.FilePath)
            .NotEmpty()
            .WithMessage("FilePath is required");

        RuleFor(x => x.FileSize)
            .GreaterThan(0)
            .WithMessage("FileSize must be greater than 0");

        RuleFor(x => x.LastModifiedDate)
            .NotEmpty()
            .WithMessage("Invalid LastModifiedDate");

        RuleFor(x => x.SourceSystem)
            .NotEmpty()
            .WithMessage("SourceSystem is required");
    }
}