using RemediationTool.Application.Models;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionResumeService
{
    Task<IngestionUploadResponse> ResumeAsync(
        string reportUid,
        CancellationToken cancellationToken = default);
}
