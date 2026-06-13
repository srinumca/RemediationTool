using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Repositories;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Read endpoints backing the demo dashboard UI — exposes the 3 demo-critical
/// tables (Reports/metadata, Success/findings, Errors/rejected rows) for the
/// "fetch all records for this job" view requested by Chaitanya.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IFileFindingRepository _fileFindingRepository;
    private readonly IRejectedRowRepository _rejectedRowRepository;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IIngestionJobAuditRepository jobAuditRepository,
        IFileFindingRepository fileFindingRepository,
        IRejectedRowRepository rejectedRowRepository,
        ILogger<DashboardController> logger)
    {
        _jobAuditRepository = jobAuditRepository;
        _fileFindingRepository = fileFindingRepository;
        _rejectedRowRepository = rejectedRowRepository;
        _logger = logger;
    }

    /// <summary>
    /// Returns all ingestion job records (Reports / file metadata table).
    /// Used to populate the "Job Metadata" view.
    /// </summary>
    [HttpGet("jobs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetJobs()
    {
        try
        {
            var jobs = _jobAuditRepository.GetAll();
            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job metadata list");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Returns a single job's metadata record. Used to refresh the upload
    /// success card with live counts after ingestion completes.
    /// </summary>
    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJob(string jobId)
    {
        try
        {
            var job = _jobAuditRepository.GetByJobId(jobId);

            if (job is null)
                return NotFound($"No job found with JobId '{jobId}'.");

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Returns all valid findings ingested under the specified job (Success table).
    /// </summary>
    [HttpGet("jobs/{jobId}/findings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFindingsByJob(string jobId)
    {
        try
        {
            var findings = _fileFindingRepository.GetByIngestionJobId(jobId);
            return Ok(findings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving findings for job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Returns all rejected rows for the specified job (Errors table).
    /// </summary>
    [HttpGet("jobs/{jobId}/rejected")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRejectedByJob(string jobId)
    {
        try
        {
            var rejected = _rejectedRowRepository.GetByJobId(jobId);
            return Ok(rejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving rejected rows for job {JobId}", jobId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Returns all rejected rows across all jobs (Errors table, unfiltered).
    /// </summary>
    [HttpGet("rejected")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAllRejected()
    {
        try
        {
            var rejected = _rejectedRowRepository.GetAll();
            return Ok(rejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all rejected rows");
            return StatusCode(500, "Internal server error");
        }
    }
}