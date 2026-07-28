using Microsoft.AspNetCore.Http;

namespace RemediationTool.API.Models;

/// <summary>
/// Multipart form payload for an EDG report upload.
/// </summary>
public sealed class UploadRequest
{
    /// <summary>CSV or XLSX report file.</summary>
    public IFormFile? File { get; set; }

    /// <summary>Source-system key selected from the configured source-system list.</summary>
    public string? SourceSystem { get; set; }
}
