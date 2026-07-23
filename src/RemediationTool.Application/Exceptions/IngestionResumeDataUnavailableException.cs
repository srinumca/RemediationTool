using RemediationTool.Application.Models;

namespace RemediationTool.Application.Exceptions;

public sealed class IngestionResumeDataUnavailableException : InvalidOperationException
{
    public IngestionResumeDataUnavailableException(
        IngestionUploadResponse response,
        Exception? innerException = null)
        : base(response.Message, innerException)
    {
        Response = response ?? throw new ArgumentNullException(nameof(response));
    }

    public IngestionUploadResponse Response { get; }
}
