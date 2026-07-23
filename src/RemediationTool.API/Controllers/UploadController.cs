using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Upload API — receives an EDG report file, stores it, creates the ingestion
/// job record, and returns the generated ReportUID for asynchronous processing.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
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
    /// Accepts an EDG CSV or XLSX report, stores it, creates the ingestion job,
    /// and returns 202 Accepted with the ReportUID.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
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

        _logger.LogInformation(
            "[UPLOAD REQUEST] FileName: {FileName} Size: {Size}",
            file.FileName,
            file.Length);

        try
        {
            var response = await _uploadService.UploadAsync(file, cancellationToken);

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
                "[UPLOAD BAD REQUEST] FileName: {FileName} Reason: {Message}",
                file.FileName,
                ex.Message);

            return BadRequest(new UploadResponse
            {
                IsSuccess = false,
                Message = ex.Message
            });
        }
    }
}
