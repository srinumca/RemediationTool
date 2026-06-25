using Microsoft.AspNetCore.Mvc;
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
    ///
    /// The actual row ingestion is triggered separately via the Ingestion API.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            var response = await _uploadService.UploadAsync(file);

            if (!response.IsSuccess)
                return BadRequest(response);

            // 202 Accepted — file received, ingestion will start via Step Function
            return Accepted(response);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Upload validation failed: {Message}", ex.Message);
            return BadRequest(new UploadResponse
            {
                IsSuccess = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed unexpectedly");
            return StatusCode(500, new UploadResponse
            {
                IsSuccess = false,
                Message = $"Upload failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Returns the current status of a report upload by ReportUID.
    /// Used by the dashboard to poll for ingestion progress.
    /// </summary>
    [HttpGet("{reportUid}")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetStatus(string reportUid)
    {
        try
        {
            var status = _uploadService.GetStatus(reportUid);

            if (status == null)
                return NotFound($"No report found with ReportUID '{reportUid}'.");

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching status for ReportUID {ReportUid}", reportUid);
            return StatusCode(500, "Internal server error");
        }
    }
}