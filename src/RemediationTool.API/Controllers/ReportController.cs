using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/report")]
public class ReportController : ControllerBase
{
    private readonly ReportService _service;
    private readonly IRejectedRowRepository _rejectedRowRepository;
    private readonly ILogger<ReportController> _logger;

    public ReportController(
        ReportService service,
        IRejectedRowRepository rejectedRowRepository,
        ILogger<ReportController> logger)
    {
        _service = service;
        _rejectedRowRepository = rejectedRowRepository;
        _logger = logger;
    }

    /// <summary>Returns all file findings.</summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        try { return Ok(_service.GetAll()); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all reports");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Returns findings filtered by status.</summary>
    [HttpGet("status/{status}")]
    public IActionResult GetByStatus(string status)
    {
        try { return Ok(_service.GetByStatus(status)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reports by status {Status}", status);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Returns findings filtered by finding type (plain string).</summary>
    [HttpGet("finding-type/{findingType}")]
    public IActionResult GetByFindingType(string findingType)
    {
        try
        {
            // Validate against allowed types
            if (!FindingType.AllAllowedTypes.Contains(findingType, StringComparer.OrdinalIgnoreCase))
                return BadRequest(
                    $"Invalid finding type '{findingType}'. " +
                    $"Allowed values: {string.Join(", ", FindingType.AllAllowedTypes)}");

            return Ok(_service.GetByFindingType(findingType));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reports by finding type {FindingType}", findingType);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Returns summary counts by finding type and status.</summary>
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        try { return Ok(_service.GetSummary()); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting summary");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("rejected-rows")]
    public IActionResult GetRejectedRows()
        => Ok(_rejectedRowRepository.GetAll());

    [HttpGet("rejected-rows/{jobId}")]
    public IActionResult GetRejectedRowsByJobId(string jobId)
        => Ok(_rejectedRowRepository.GetByJobId(jobId));
}