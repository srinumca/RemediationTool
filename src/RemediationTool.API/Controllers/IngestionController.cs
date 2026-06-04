using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enum;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IngestionService _ingestionService;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IRejectedRowRepository _rejectedRowRepository;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IngestionService ingestionService,
        IIngestionJobAuditRepository jobAuditRepository,
        IRejectedRowRepository rejectedRowRepository,
        ILogger<IngestionController> logger)
    {
        _ingestionService = ingestionService;
        _jobAuditRepository = jobAuditRepository;
        _rejectedRowRepository = rejectedRowRepository;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Upload / Resume
    // -------------------------------------------------------------------------

    /// <summary>Uploads a CSV or XLSX file for bulk ingestion.</summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
       
        try
        {
            var response = await _ingestionService.ProcessAsync(file);

            if (response.Status == IngestionJobStatus.Failed && response.TotalRecords == 0)
                return BadRequest(response);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex.Message) ;
            return StatusCode(500, new IngestionUploadResponse
            {
                Status = IngestionJobStatus.Failed,
                Message = $"An error occurred while processing the file: {ex.StackTrace}"
            });
        }
    }

    /// <summary>Resumes a previously failed or partial ingestion job from its last checkpoint.</summary>
    [HttpPost("resume/{jobId}")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Resume(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest("JobId is required.");

        var result = await _ingestionService.ResumeAsync(jobId);
        return Ok(result);
    }

    // -------------------------------------------------------------------------
    // Ingestion Success audit report (Req 7)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the ingestion audit log for all jobs — the "Ingestion Success" report.
    /// Includes: JobId, InboundFileName, UserName, StartTimestampUtc, EndTimestampUtc,
    /// SourceSystem, TriggerType, IngestionMode, PayloadRecordCount, SuccessCount,
    /// RejectCount, Status, FindingTypeCounts.
    /// </summary>
    [HttpGet("jobs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAllJobs()
    {
        try
        {
            var audits = _jobAuditRepository.GetAll()
                .OrderByDescending(a => a.StartTimestampUtc)
                .ToList();

            return Ok(audits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ingestion job audit list");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Returns the full audit record for a specific ingestion job.
    /// </summary>
    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJobById(string jobId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return BadRequest("JobId is required.");

            var audit = _jobAuditRepository.GetByJobId(jobId);

            if (audit == null)
                return NotFound($"No ingestion job found with JobId '{jobId}'.");

            return Ok(audit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ingestion job audit for JobId {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    // -------------------------------------------------------------------------
    // Ingestion Failure audit report (Req 8)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all rejected rows for a specific ingestion job — the "Ingestion Failure" report.
    /// Provides per-row detail: RejectedRowId (ID), InboundFileName, FindingFileName,
    /// UserName, FindingType, ErrorDateUtc, ErrorReason (validation failure specific),
    /// FieldName, RejectedValue, RowNumber, RawRowJson.
    /// </summary>
    [HttpGet("jobs/{jobId}/rejected-rows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetRejectedRowsByJobId(string jobId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return BadRequest("JobId is required.");

            // Verify the job exists first — return 404 if the jobId is unknown
            var job = _jobAuditRepository.GetByJobId(jobId);
            if (job == null)
                return NotFound($"No ingestion job found with JobId '{jobId}'.");

            var rejectedRows = _rejectedRowRepository.GetByJobId(jobId);
            return Ok(rejectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving rejected rows for JobId {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }
}