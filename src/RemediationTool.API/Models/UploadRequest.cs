using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RemediationTool.API.Models;

/// <summary>
/// Multipart form payload for an EDG report upload.
/// </summary>
public sealed class UploadRequest
{
    /// <summary>CSV or XLSX report file.</summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    /// <summary>Required source-system key submitted with the upload.</summary>
    [FromForm(Name = "sourceSystem")]
    public string? SourceSystem { get; set; }
}
