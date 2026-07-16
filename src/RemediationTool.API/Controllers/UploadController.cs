using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enum;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Upload API — receives the EDG report file, saves it to S3,
/// creates the report record in DynamoDB, and returns immediately
/// with a ReportUID and 202 Accepted.
///
/// Does NOT process rows. That is the Ingestion API's job.
///
/// Flow:
///   UI → POST /api/upload → S3 (save file) → DynamoDB (create record)
///   → return 202 with ReportUID → Step Function picks up and calls Ingestion API
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
    /// Accepts an EDG report file (CSV or XLSX), saves it to S3,
    /// creates a report record in DynamoDB with status = NotYetStarted,
    /// and returns 202 Accepted immediately with the ReportUID.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[UPLOAD REQUEST] FileName: {FileName} Size: {Size}",
            file?.FileName, file?.Length);

        try
        {
            var response = await _uploadService.UploadAsync(file, cancellationToken);

            if (!response.IsSuccess)
            {
                _logger.LogWarning(
                    "[UPLOAD RESPONSE] FileName: {FileName} — returned 400 BadRequest. Message: {Message}",
                    file?.FileName, response.Message);
                return BadRequest(response);
            }

            _logger.LogInformation(
                "[UPLOAD RESPONSE] ReportUid: {ReportUid} — returned 202 Accepted.",
                response.ReportUid);
            return Accepted(response);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex,
                "[UPLOAD BAD REQUEST] FileName: {FileName} Reason: {Message}",
                file?.FileName, ex.Message);
            return BadRequest(new UploadResponse
            {
                IsSuccess = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Returns the current status of a report upload by ReportUID.
    /// Used by the dashboard to poll for ingestion progress.
    /// </summary>
    [HttpGet("{reportUid}")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetStatus(string reportUid)
    {
        _logger.LogInformation("[UPLOAD STATUS REQUEST] ReportUid: {ReportUid}", reportUid);

        var status = _uploadService.GetStatus(reportUid);

        if (status == null)
        {
            _logger.LogWarning("[UPLOAD STATUS NOT FOUND] ReportUid: {ReportUid}", reportUid);
            return NotFound($"No report found with ReportUID '{reportUid}'.");
        }

        return Ok(status);
    }
}
