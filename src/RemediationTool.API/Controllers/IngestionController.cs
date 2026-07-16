using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enum;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Ingestion API for direct dashboard ingestion and Step Function processing.
/// User-triggered direct ingestion requires Admin access. Job-based processing,
/// resume, and status endpoints require the configured Entra application role.
/// </summary>
[ApiController]
[Route("api/ingestion")]
public class IngestionController : ControllerBase
{
    private readonly IngestionService _ingestionService;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IngestionService ingestionService,
        ILogger<IngestionController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Uploads and ingests a file in the same request for the retained dashboard flow.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminAccess)]
    [HttpPost("upload")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadAndIngest(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[INGESTION_DIRECT_UPLOAD_REQUEST] FileName:{FileName}, Size:{Size}",
            file?.FileName,
            file?.Length);

        try
        {
            var response = await _ingestionService.ProcessAsync(file, cancellationToken);

            _logger.LogInformation(
                "[INGESTION_DIRECT_UPLOAD_RESPONSE] JobId:{JobId}, Status:{Status}, Total:{Total}, Success:{Success}, Rejected:{Rejected}",
                response.JobId,
                response.Status,
                response.TotalRecords,
                response.SuccessCount,
                response.RejectCount);

            if (response.Status == IngestionJobStatus.Failed && response.TotalRecords == 0)
                return UnprocessableEntity(response);

            return Ok(response);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(
                ex,
                "[INGESTION_DIRECT_UPLOAD_BAD_REQUEST] FileName:{FileName}, Reason:{Reason}",
                file?.FileName,
                ex.Message);

            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Policy = AuthorizationPolicies.InternalApplication)]
    [HttpPost("{reportUid}")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Ingest(
        string reportUid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return BadRequest("ReportUID is required.");

        _logger.LogInformation("[INGESTION REQUEST] ReportUid: {ReportUid} — ingestion triggered.", reportUid);

        try
        {
            var response = await _ingestionService.IngestAsync(reportUid, cancellationToken);

            _logger.LogInformation(
                "[INGESTION RESPONSE] ReportUid: {ReportUid} Status: {Status} Total: {Total} Success: {Success} Rejected: {Rejected}",
                reportUid, response.Status, response.TotalRecords, response.SuccessCount, response.RejectCount);

            if (response.Status == IngestionJobStatus.Failed && response.TotalRecords == 0)
                return UnprocessableEntity(response);

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("[INGESTION NOT FOUND] ReportUid: {ReportUid} — no job record found.", reportUid);
            return NotFound($"No report found with ReportUID '{reportUid}'.");
        }
    }

    [Authorize(Policy = AuthorizationPolicies.InternalApplication)]
    [HttpPost("{reportUid}/resume")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Resume(
        string reportUid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return BadRequest("ReportUID is required.");

        _logger.LogInformation("[INGESTION RESUME REQUEST] ReportUid: {ReportUid}", reportUid);

        try
        {
            var response = await _ingestionService.ResumeAsync(reportUid, cancellationToken);

            _logger.LogInformation(
                "[INGESTION RESUME RESPONSE] ReportUid: {ReportUid} Status: {Status}",
                reportUid, response.Status);

            if (response.Status == IngestionJobStatus.Failed && response.TotalRecords == 0)
                return UnprocessableEntity(response);

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("[INGESTION RESUME NOT FOUND] ReportUid: {ReportUid}", reportUid);
            return NotFound($"No checkpoint found for ReportUID '{reportUid}'.");
        }
    }

    [Authorize(Policy = AuthorizationPolicies.InternalApplication)]
    [HttpGet("{reportUid}/status")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetStatus(string reportUid)
    {
        _logger.LogInformation("[INGESTION STATUS REQUEST] ReportUid: {ReportUid}", reportUid);

        var status = _ingestionService.GetStatus(reportUid);

        if (status == null)
        {
            _logger.LogWarning("[INGESTION STATUS NOT FOUND] ReportUid: {ReportUid}", reportUid);
            return NotFound($"No ingestion job found for ReportUID '{reportUid}'.");
        }

        return Ok(status);
    }
}
