using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Repositories;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Read endpoints backing the dashboard UI.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ReadAccess)]
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

    [HttpGet("jobs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    [HttpGet("jobs/{jobId}/findings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    [HttpGet("jobs/{jobId}/rejected")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    [HttpGet("rejected")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
