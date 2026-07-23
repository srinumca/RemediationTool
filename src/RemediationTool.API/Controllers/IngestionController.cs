using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Exceptions;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enum;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Processes and resumes previously uploaded EDG reports by ReportUID.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.InternalApplication)]
[ApiController]
[Route("api/ingestion")]
public class IngestionController : ControllerBase
{
    private readonly IngestionService _ingestionService;
    private readonly IIngestionResumeService? _ingestionResumeService;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IngestionService ingestionService,
        ILogger<IngestionController> logger,
        IIngestionResumeService? ingestionResumeService = null)
    {
        _ingestionService = ingestionService;
        _logger = logger;
        _ingestionResumeService = ingestionResumeService;
    }

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

        _logger.LogInformation(
            "[INGESTION REQUEST] ReportUid: {ReportUid} — ingestion triggered.",
            reportUid);

        try
        {
            var response = await _ingestionService.IngestAsync(
                reportUid,
                cancellationToken);

            _logger.LogInformation(
                "[INGESTION RESPONSE] ReportUid: {ReportUid} Status: {Status} Total: {Total} Success: {Success} Rejected: {Rejected}",
                reportUid,
                response.Status,
                response.TotalRecords,
                response.SuccessCount,
                response.RejectCount);

            if (response.Status == IngestionJobStatus.Failed
                && response.TotalRecords == 0)
            {
                return UnprocessableEntity(response);
            }

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning(
                "[INGESTION NOT FOUND] ReportUid: {ReportUid} — no job record found.",
                reportUid);
            return NotFound(
                $"No report found with ReportUID '{reportUid}'.");
        }
    }

    /// <summary>
    /// Resumes an eligible failed ingestion job from its latest checkpoint.
    /// </summary>
    [HttpPost("{reportUid}/resume")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Resume(
        string reportUid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return BadRequest("ReportUID is required.");

        if (_ingestionResumeService == null)
        {
            _logger.LogError(
                "[INGESTION RESUME CONFIGURATION ERROR] Resume service is not registered.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "Ingestion resume service is not available.");
        }

        _logger.LogInformation(
            "[INGESTION RESUME REQUEST] ReportUid: {ReportUid}",
            reportUid);

        try
        {
            var response = await _ingestionResumeService.ResumeAsync(
                reportUid,
                cancellationToken);

            _logger.LogInformation(
                "[INGESTION RESUME RESPONSE] ReportUid: {ReportUid} Status: {Status} ResumeEligible: {ResumeEligible} LastProcessedRecordCount: {LastProcessedRecordCount}",
                reportUid,
                response.Status,
                response.IsResumeEligible,
                response.LastProcessedRecordCount);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "[INGESTION RESUME NOT FOUND] ReportUid: {ReportUid}",
                reportUid);

            return NotFound(CreateFailedResumeResponse(reportUid, ex.Message));
        }
        catch (IngestionResumeDataUnavailableException ex)
        {
            _logger.LogWarning(
                ex,
                "[INGESTION RESUME DATA UNAVAILABLE] ReportUid: {ReportUid}",
                reportUid);

            return UnprocessableEntity(ex.Response);
        }
    }

    private static IngestionUploadResponse CreateFailedResumeResponse(
        string reportUid,
        string message)
    {
        var completedAtUtc = DateTime.UtcNow;
        return new IngestionUploadResponse
        {
            ReportUid = reportUid,
            JobId = reportUid,
            Status = IngestionJobStatus.Failed,
            StartedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
            IsResumeEligible = false,
            Message = message
        };
    }
}
