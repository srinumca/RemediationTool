using RemediationTool.Application.Services;
using RemediationTool.Domain.Enums;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class ErrorCategoryResolverTests
{
    public static TheoryData<Exception, ErrorCategory> ExceptionCases => new()
    {
        { new UnauthorizedAccessException("denied"), ErrorCategory.PermissionDenied },
        { new IOException("sharing violation"), ErrorCategory.FileLockedOrCheckedOut },
        { new IOException("file is locked"), ErrorCategory.FileLockedOrCheckedOut },
        { new IOException("checked out by another user"), ErrorCategory.FileLockedOrCheckedOut },
        { new IOException("file is in use"), ErrorCategory.FileLockedOrCheckedOut },
        { new IOException("being used by another process"), ErrorCategory.FileLockedOrCheckedOut },
        { new IOException("read-only file"), ErrorCategory.ReadOnlyOrSystemProtected },
        { new IOException("readonly file"), ErrorCategory.ReadOnlyOrSystemProtected },
        { new IOException("write protected volume"), ErrorCategory.ReadOnlyOrSystemProtected },
        { new IOException("access is denied"), ErrorCategory.ReadOnlyOrSystemProtected },
        { new IOException("network path unavailable"), ErrorCategory.AuthenticationOrCertificateFailure },
        { new IOException("host not found"), ErrorCategory.AuthenticationOrCertificateFailure },
        { new IOException("connection refused"), ErrorCategory.AuthenticationOrCertificateFailure },
        { new IOException("certificate validation failed"), ErrorCategory.AuthenticationOrCertificateFailure },
        { new IOException("token expired"), ErrorCategory.AuthenticationOrCertificateFailure },
        { new IOException("request throttled"), ErrorCategory.RateLimitingOrThrottling },
        { new IOException("rate limit exceeded"), ErrorCategory.RateLimitingOrThrottling },
        { new IOException("too many requests"), ErrorCategory.RateLimitingOrThrottling },
        { new IOException("HTTP 429"), ErrorCategory.RateLimitingOrThrottling },
        { new NotSupportedException("unsupported"), ErrorCategory.UnsupportedFileType },
        { new TimeoutException("timeout"), ErrorCategory.AuthenticationOrCertificateFailure },
        { new OperationCanceledException("cancelled"), ErrorCategory.RetryExhausted },
        { new InvalidOperationException("service unavailable"), ErrorCategory.RestorationSystemUnavailable },
        { new InvalidOperationException("target already exists"), ErrorCategory.RestorationTargetConflict },
        { new InvalidDataException("bad row"), ErrorCategory.MalformedInputRow },
        { new Exception("unknown"), ErrorCategory.Others },
        { new IOException("generic I/O failure"), ErrorCategory.Others }
    };

    [Theory]
    [MemberData(nameof(ExceptionCases))]
    public void FromException_ReturnsExpectedCategory(
        Exception exception,
        ErrorCategory expected)
    {
        var actual = ErrorCategoryResolver.FromException(exception);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Field", "Value is required", ErrorCategory.MissingRequiredField)]
    [InlineData("Field", "Value must not be empty", ErrorCategory.MissingRequiredField)]
    [InlineData("Field", "Value is invalid", ErrorCategory.InvalidAllowedValue)]
    [InlineData("Field", "Value must be one of A, B", ErrorCategory.InvalidAllowedValue)]
    [InlineData("Field", "Date is invalid", ErrorCategory.InvalidAllowedValue)]
    [InlineData("Field", "Expected number", ErrorCategory.InvalidDataType)]
    [InlineData("Field", "Invalid size format", ErrorCategory.InvalidAllowedValue)]
    [InlineData(null, null, ErrorCategory.ValidationError)]
    [InlineData("Field", null, ErrorCategory.ValidationError)]
    public void ValidationFailure_ReturnsExpectedCategory(
        string? propertyName,
        string? errorMessage,
        ErrorCategory expected)
    {
        var actual = ErrorCategoryResolver.ValidationFailure(propertyName, errorMessage);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PreCheckResolvers_ReturnRequirementCategories()
    {
        Assert.Equal(ErrorCategory.MalformedInputRow, ErrorCategoryResolver.MalformedInputRow());
        Assert.Equal(ErrorCategory.MissingAtSource, ErrorCategoryResolver.SourceFileMissing());
        Assert.Equal(ErrorCategory.RestorationQuarantineFileMissing, ErrorCategoryResolver.QuarantineFileMissing());
        Assert.Equal(ErrorCategory.RestorationTargetPathMissing, ErrorCategoryResolver.TargetPathMissing());
        Assert.Equal(ErrorCategory.RestorationMetadataIntegrityFailure, ErrorCategoryResolver.MetadataIntegrityFailure());
        Assert.Equal(ErrorCategory.RestorationDuplicateRestoreAttempt, ErrorCategoryResolver.DuplicateRestoreAttempt());
        Assert.Equal(ErrorCategory.RestorationPartialRestoreFailure, ErrorCategoryResolver.PartialRestoreFailure());
        Assert.Equal(ErrorCategory.RestorationTargetConflict, ErrorCategoryResolver.RestoreTargetConflict());
        Assert.Equal(ErrorCategory.DeletionRetentionNotMet, ErrorCategoryResolver.RetentionNotMet());
        Assert.Equal(ErrorCategory.DeletionQuarantineFileMissing, ErrorCategoryResolver.DeleteQuarantineFileMissing());
        Assert.Equal(ErrorCategory.DeletionDuplicateAttempt, ErrorCategoryResolver.DuplicateDeleteAttempt());
        Assert.Equal(ErrorCategory.DeletionPartialFailure, ErrorCategoryResolver.PartialDeleteFailure());
    }
}
