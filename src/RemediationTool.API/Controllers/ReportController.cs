using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;


namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/report")]
public class ReportController : ControllerBase
{
    private readonly ReportService _service;
    private readonly ILogger<ReportController> _logger;
    private readonly ReportService _reportService;
    private readonly IRejectedRowRepository _rejectedRowRepository;

    public ReportController(ReportService service, ILogger<ReportController> logger, ReportService reportService, IRejectedRowRepository rejectedRowRepository)
    {
        _service = service;
        _logger = logger;
        _reportService = reportService;
        _rejectedRowRepository = rejectedRowRepository;
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

    [HttpGet("rejected-rows")]
    public IActionResult GetRejectedRows()
    {
        var rows = _rejectedRowRepository.GetAll();
        return Ok(rows);
    }

    [HttpGet("rejected-rows/{jobId}")]
    public IActionResult GetRejectedRowsByJobId(string jobId)
    {
        var rows = _rejectedRowRepository.GetByJobId(jobId);
        return Ok(rows);
    }
}