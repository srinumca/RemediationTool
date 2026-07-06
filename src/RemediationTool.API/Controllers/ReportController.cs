using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Models;
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

    [HttpGet]
    public IActionResult GetAll()
    {
        _logger.LogInformation("[REPORT REQUEST] GetAll");
        return Ok(_service.GetAll());
    }

    [HttpGet("status/{status}")]
    public IActionResult GetByStatus(string status)
    {
        _logger.LogInformation("[REPORT REQUEST] GetByStatus Status: {Status}", status);
        return Ok(_service.GetByStatus(status));
    }

    [HttpGet("finding-type/{findingType}")]
    public IActionResult GetByFindingType(string findingType)
    {
        _logger.LogInformation("[REPORT REQUEST] GetByFindingType FindingType: {FindingType}", findingType);

        if (!FindingType.AllAllowedTypes.Contains(findingType, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[REPORT BAD REQUEST] Invalid finding type '{FindingType}'.", findingType);
            return BadRequest(
                $"Invalid finding type '{findingType}'. " +
                $"Allowed values: {string.Join(", ", FindingType.AllAllowedTypes)}");
        }

        return Ok(_service.GetByFindingType(findingType));
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        _logger.LogInformation("[REPORT REQUEST] GetSummary");
        return Ok(_service.GetSummary());
    }

    [HttpGet("dashboard/summary")]
    public IActionResult GetDashboardSummary()
    {
        _logger.LogInformation("[REPORT REQUEST] GetDashboardSummary");
        var data = _service.GetDashboardSummary();
        return Ok(ApiResponse<object>.Ok(data, "Dashboard summary loaded.", HttpContext.TraceIdentifier));
    }

    [HttpGet("dashboard/tab/{tab}")]
    public IActionResult GetDashboardTab(string tab)
    {
        _logger.LogInformation("[REPORT REQUEST] GetDashboardTab Tab:{Tab}", tab);
        var data = _service.GetByWorkflowTab(tab);
        return Ok(ApiResponse<object>.Ok(data, "Dashboard tab data loaded.", HttpContext.TraceIdentifier));
    }

    [HttpGet("export/csv")]
    public IActionResult ExportCsv([FromQuery] string? tab = null, [FromQuery] string? status = null, [FromQuery] string? findingType = null)
    {
        _logger.LogInformation(
            "[REPORT REQUEST] ExportCsv Tab:{Tab}, Status:{Status}, FindingType:{FindingType}",
            tab,
            status,
            findingType);

        var csvBytes = _service.ExportCsv(tab, status, findingType);
        var fileName = $"remediation-report-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(csvBytes, "text/csv", fileName);
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
