using Microsoft.AspNetCore.Http;
using RemediationTool.Application.Models;

namespace RemediationTool.Application.Interfaces;

public interface IIngestionService
{
    Task<IngestionUploadResponse> ProcessAsync(IFormFile file);

    Task<IngestionUploadResponse> ResumeAsync(string jobId);
}