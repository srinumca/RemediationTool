using RemediationTool.Application.Constants;
using RemediationTool.Application.Validators;
using RemediationTool.Domain.Entities;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class FileFindingValidatorTests
{
    private readonly FileFindingValidator _validator = new();

    [Fact]
    public void Validate_AcceptsCompleteObsoleteFinding()
    {
        var result = _validator.Validate(CreateValidFinding());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ReportsEveryMissingRequiredField()
    {
        var finding = CreateValidFinding();
        finding.FindingFileName = string.Empty;
        finding.FindingFileFormat = string.Empty;
        finding.CurrentFileLocation = string.Empty;
        finding.FindingType = string.Empty;
        finding.OriginatingDataSystem = string.Empty;
        finding.OriginatingVendorTool = string.Empty;

        var result = _validator.Validate(finding);
        var failedProperties = result.Errors
            .Select(error => error.PropertyName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.False(result.IsValid);
        Assert.Contains(nameof(FileFinding.FindingFileName), failedProperties);
        Assert.Contains(nameof(FileFinding.FindingFileFormat), failedProperties);
        Assert.Contains(nameof(FileFinding.CurrentFileLocation), failedProperties);
        Assert.Contains(nameof(FileFinding.FindingType), failedProperties);
        Assert.Contains(nameof(FileFinding.OriginatingDataSystem), failedProperties);
        Assert.Contains(nameof(FileFinding.OriginatingVendorTool), failedProperties);
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("Blob")]
    [InlineData("Unknown")]
    public void Validate_RejectsUnsupportedOriginatingDataSystem(string dataSystem)
    {
        var finding = CreateValidFinding();
        finding.OriginatingDataSystem = dataSystem;

        var result = _validator.Validate(finding);

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(FileFinding.OriginatingDataSystem));
    }

    [Fact]
    public void Validate_RejectsUnsupportedFindingType()
    {
        var finding = CreateValidFinding();
        finding.FindingType = "PendingReview";

        var result = _validator.Validate(finding);

        var error = Assert.Single(
            result.Errors,
            item => item.PropertyName == nameof(FileFinding.FindingType));
        Assert.Contains("Allowed values", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RequiresQuarantineLocationAndDateForQuarantinedFinding()
    {
        var finding = CreateValidFinding();
        finding.FindingType = FindingType.Quarantined;
        finding.OriginalFileLocation = null;
        finding.QuarantineDateUtc = null;

        var invalidResult = _validator.Validate(finding);

        Assert.Contains(
            invalidResult.Errors,
            error => error.PropertyName == nameof(FileFinding.OriginalFileLocation));
        Assert.Contains(
            invalidResult.Errors,
            error => error.PropertyName == nameof(FileFinding.QuarantineDateUtc));

        finding.OriginalFileLocation = @"\\server\share\source.txt";
        finding.QuarantineDateUtc = DateTime.UtcNow.AddDays(-1);

        var validResult = _validator.Validate(finding);

        Assert.True(validResult.IsValid);
    }

    [Fact]
    public void Validate_RequiresExceptionDateForExceptionFinding()
    {
        var finding = CreateValidFinding();
        finding.FindingType = FindingType.Exception;
        finding.ExceptionDateUtc = null;

        var invalidResult = _validator.Validate(finding);

        Assert.Contains(
            invalidResult.Errors,
            error => error.PropertyName == nameof(FileFinding.ExceptionDateUtc));

        finding.ExceptionDateUtc = DateTime.UtcNow;

        Assert.True(_validator.Validate(finding).IsValid);
    }

    [Theory]
    [InlineData("Severe", null)]
    [InlineData("Low", "not-an-email")]
    public void Validate_RejectsInvalidOptionalEnumAndEmail(
        string riskLevel,
        string? requestorEmail)
    {
        var finding = CreateValidFinding();
        finding.RiskLevel = riskLevel;
        finding.RestorationRequestorEmail = requestorEmail;

        var result = _validator.Validate(finding);

        if (riskLevel == "Severe")
        {
            Assert.Contains(
                result.Errors,
                error => error.PropertyName == nameof(FileFinding.RiskLevel));
        }

        if (requestorEmail is not null)
        {
            Assert.Contains(
                result.Errors,
                error => error.PropertyName == nameof(FileFinding.RestorationRequestorEmail));
        }
    }

    [Fact]
    public void Validate_RejectsFutureMetadataDates()
    {
        var future = DateTime.UtcNow.AddDays(3);
        var finding = CreateValidFinding();
        finding.LastModifiedDateUtc = future;
        finding.CreatedDateUtc = future;
        finding.LastAccessedDateUtc = future;
        finding.DetectionDateUtc = future;

        var result = _validator.Validate(finding);
        var failedProperties = result.Errors
            .Select(error => error.PropertyName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(FileFinding.LastModifiedDateUtc), failedProperties);
        Assert.Contains(nameof(FileFinding.CreatedDateUtc), failedProperties);
        Assert.Contains(nameof(FileFinding.LastAccessedDateUtc), failedProperties);
        Assert.Contains(nameof(FileFinding.DetectionDateUtc), failedProperties);
    }

    [Fact]
    public void Validate_RejectsNegativeFileSize()
    {
        var finding = CreateValidFinding();
        finding.FindingFileSizeBytes = -1;

        var result = _validator.Validate(finding);

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(FileFinding.FindingFileSizeBytes));
    }

    private static FileFinding CreateValidFinding()
    {
        return new FileFinding
        {
            FindingFileName = "source.txt",
            FindingFileFormat = "txt",
            FindingFileSizeBytes = 1024,
            CurrentFileLocation = @"\\server\share\source.txt",
            FindingType = FindingType.Obsolete,
            OriginatingDataSystem = "SMB",
            OriginatingVendorTool = "EDG",
            LastModifiedDateUtc = DateTime.UtcNow.AddYears(-10),
            RiskLevel = "Low"
        };
    }
}
