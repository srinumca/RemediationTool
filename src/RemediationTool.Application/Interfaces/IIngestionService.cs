using RemediationTool.Application.Models;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionService
{
    Task<IngestionUploadResponse> IngestAsync(
        string reportUid,
        CancellationToken cancellationToken = default);

    Task<IngestionUploadResponse> ResumeAsync(
        string reportUid,
        CancellationToken cancellationToken = default);
}
