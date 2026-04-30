using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Services;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/report")]
public class ReportController : ControllerBase
{
    private readonly ReportService _service;
    private readonly ILogger<ReportController> _logger;

    public ReportController(ReportService service, ILogger<ReportController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // Get all files
    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            return Ok(_service.GetAll());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all reports");
            return StatusCode(500, "Internal server error");
        }
    }

    // Get by status
    [HttpGet("status/{status}")]
    public IActionResult GetByStatus(string status)
    {
        try
        {
            return Ok(_service.GetByStatus(status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reports by status {Status}", status);
            return StatusCode(500, "Internal server error");
        }
    }

    // Summary
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        try
        {
            return Ok(_service.GetSummary());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting summary");
            return StatusCode(500, "Internal server error");
        }
    }
}