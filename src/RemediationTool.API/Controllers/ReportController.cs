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
        _logger.LogInformation("[REPORT REQUEST] GetAll");
        return Ok(_service.GetAll());
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    /// <summary>Returns findings filtered by status.</summary>
    [HttpGet("status/{status}")]
    public IActionResult GetByStatus(string status)
    {
        _logger.LogInformation("[REPORT REQUEST] GetByStatus Status: {Status}", status);
        return Ok(_service.GetByStatus(status));
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    /// <summary>Returns findings filtered by finding type (plain string).</summary>
    [HttpGet("finding-type/{findingType}")]
    public IActionResult GetByFindingType(string findingType)
    {
        _logger.LogInformation("[REPORT REQUEST] GetByFindingType FindingType: {FindingType}", findingType);

        // Validate against allowed types
        if (!FindingType.AllAllowedTypes.Contains(findingType, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[REPORT BAD REQUEST] Invalid finding type '{FindingType}'.", findingType);
            return BadRequest(
                $"Invalid finding type '{findingType}'. " +
                $"Allowed values: {string.Join(", ", FindingType.AllAllowedTypes)}");
        }

        return Ok(_service.GetByFindingType(findingType));
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    /// <summary>Returns summary counts by finding type and status.</summary>
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        _logger.LogInformation("[REPORT REQUEST] GetSummary");
        return Ok(_service.GetSummary());
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    [HttpGet("rejected-rows")]
    public IActionResult GetRejectedRows()
    {
        _logger.LogInformation("[REPORT REQUEST] GetRejectedRows (all)");
        return Ok(_rejectedRowRepository.GetAll());
    }

    [HttpGet("rejected-rows/{jobId}")]
    public IActionResult GetRejectedRowsByJobId(string jobId)
    {
        _logger.LogInformation("[REPORT REQUEST] GetRejectedRowsByJobId JobId: {JobId}", jobId);
        return Ok(_rejectedRowRepository.GetByJobId(jobId));
    }
}