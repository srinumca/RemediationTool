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
    public async Task<IActionResult> Ingest(string reportUid)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return BadRequest("ReportUID is required.");

        try
        {
            var response = await _ingestionService.IngestAsync(reportUid);

            if (response.Status == IngestionJobStatus.Failed && response.TotalRecords == 0)
                return UnprocessableEntity(response);

            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"No report found with ReportUID '{reportUid}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for ReportUID {ReportUid}", reportUid);
            return StatusCode(500, new IngestionUploadResponse
            {
                ReportUid = reportUid,
                Status = IngestionJobStatus.Failed,
                Message = $"Ingestion failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Resumes a failed or partially-completed ingestion job
    /// from the last successful checkpoint.
    /// No re-upload required — reads remaining records from
    /// the Parquet working file or DynamoDB staging table.
    /// </summary>
    [HttpPost("{reportUid}/resume")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(string reportUid)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return BadRequest("ReportUID is required.");

        try
        {
            var result = await _ingestionService.ResumeAsync(reportUid);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"No checkpoint found for ReportUID '{reportUid}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume failed for ReportUID {ReportUid}", reportUid);
            return StatusCode(500, $"Resume failed: {ex.Message}");
        }
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
        try
        {
            var status = _ingestionService.GetStatus(reportUid);

            if (status == null)
                return NotFound($"No ingestion job found for ReportUID '{reportUid}'.");

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ingestion status for {ReportUid}", reportUid);
            return StatusCode(500, "Internal server error");
        }
    }
}