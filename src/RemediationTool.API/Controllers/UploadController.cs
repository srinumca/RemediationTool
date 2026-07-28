using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Upload API — receives an EDG report file and source system, stores the file,
/// creates the ingestion job record, and returns the generated ReportUID.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.InternalApplication)]
[ApiController]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private readonly UploadService _uploadService;
    private readonly ILogger<UploadController> _logger;

    public UploadController(
        UploadService uploadService,
        ILogger<UploadController> logger)
    {
        _uploadService = uploadService;
        _logger = logger;
    }

    /// <summary>
    /// Accepts an EDG CSV or XLSX report and a source-system value, stores the file,
    /// creates the ingestion job, and returns 202 Accepted with the ReportUID.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? sourceSystem,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            _logger.LogWarning("[UPLOAD BAD REQUEST] No file was provided.");
            return BadRequest(new UploadResponse
            {
                IsSuccess = false,
                Message = "A file is required."
            });
        }

        if (string.IsNullOrWhiteSpace(sourceSystem))
        {
            _logger.LogWarning(
                "[UPLOAD BAD REQUEST] FileName: {FileName} — no source system was provided.",
                file.FileName);
            return BadRequest(new UploadResponse
            {
                IsSuccess = false,
                Message = "Source system is required."
            });
        }

        _logger.LogInformation(
            "[UPLOAD REQUEST] FileName: {FileName} Size: {Size} SourceSystem: {SourceSystem}",
            file.FileName,
            file.Length,
            sourceSystem);

        try
        {
            var response = await _uploadService.UploadAsync(
                file,
                sourceSystem,
                cancellationToken);

            if (!response.IsSuccess)
            {
                _logger.LogWarning(
                    "[UPLOAD RESPONSE] FileName: {FileName} — returned 400 BadRequest. Message: {Message}",
                    file.FileName,
                    response.Message);
                return BadRequest(response);
            }

            _logger.LogInformation(
                "[UPLOAD RESPONSE] ReportUid: {ReportUid} — returned 202 Accepted.",
                response.ReportUid);
            return Accepted(response);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(
                ex,
                "[UPLOAD BAD REQUEST] FileName: {FileName} SourceSystem: {SourceSystem} Reason: {Message}",
                file.FileName,
                sourceSystem,
                ex.Message);

            return BadRequest(new UploadResponse
            {
                IsSuccess = false,
                Message = ex.Message
            });
        }
    }
}
