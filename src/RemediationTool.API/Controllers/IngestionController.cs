using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enum;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Ingestion API — processes rows from an already-uploaded EDG report.
///
/// This controller is called by the AWS Step Function AFTER the
/// Upload API has saved the file to S3 and created the report record.
///
/// It is NOT called directly by the UI.
///
/// Flow:
///   Step Function → POST /api/ingestion/{reportUid}
///   → read file from S3
///   → parse + validate rows
///   → save findings to DynamoDB
///   → save rejected rows to DynamoDB
///   → update report status = Success / PartialSuccess / Failed
///   → return result to Step Function
///
/// NOTE: Unhandled exceptions are now caught once by GlobalExceptionMiddleware
/// (registered in Program.cs). The try/catch below remains only for the
/// KeyNotFoundException case, which this controller wants to map to a 404
/// instead of the default 500.
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
    /// Processes all rows from the already-uploaded EDG report.
    /// Called by Step Function — not the UI directly.
    /// Reads the file from S3, validates rows, and writes to DynamoDB.
    /// Returns 200 on success or partial success, 422 on full failure.
    /// </summary>
    [HttpPost("{reportUid}")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status422UnprocessableEntity)]
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
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
        // IngestionService.IngestAsync already catches its own internal
        // failures and returns a Status=Failed response rather than
        // throwing, so a true exception reaching here means something
        // truly unexpected happened (e.g. DI failure, OOM).
    }

    /// <summary>
    /// Resumes a failed or partially-completed ingestion job
    /// from the last successful checkpoint.
    /// </summary>
    [HttpPost("{reportUid}/resume")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status422UnprocessableEntity)]
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
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    /// <summary>
    /// Returns the current ingestion status for a report.
    /// Used by Step Function to poll for completion.
    /// </summary>
    [HttpGet("{reportUid}/status")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
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
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }
}
