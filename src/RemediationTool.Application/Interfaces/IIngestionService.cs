using Microsoft.AspNetCore.Http;
using RemediationTool.Application.Models;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionService
{
    Task<IngestionUploadResponse> ProcessAsync(
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<IngestionUploadResponse> ResumeAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
