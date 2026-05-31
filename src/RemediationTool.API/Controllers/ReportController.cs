using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Repositories;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enums;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/report")]
public class ReportController : ControllerBase
{
    private readonly ReportService _reportService;
    private readonly IRejectedRowRepository _rejectedRowRepository;
    private readonly ILogger<ReportController> _logger;

    public ReportController(
        ReportService reportService,
        IRejectedRowRepository rejectedRowRepository,
        ILogger<ReportController> logger)
    {
        _reportService = reportService;
        _rejectedRowRepository = rejectedRowRepository;
        _logger = logger;
    }

    /// <summary>Returns the most recent record per finding for the given FindingType.</summary>
    [HttpGet("finding-type/{findingType}")]
    public IActionResult GetByFindingType(string findingType)
    {
        try
        {
            if (!Enum.TryParse<FindingType>(findingType, ignoreCase: true, out var parsed))
                return BadRequest($"Invalid finding type '{findingType}'. Valid values: {string.Join(", ", Enum.GetNames<FindingType>())}");

            return Ok(_reportService.GetByFindingType(parsed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting report by finding type {FindingType}", findingType);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Returns counts of the most recent record per finding grouped by FindingType. Used by dashboard KPI cards.</summary>
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        try
        {
            return Ok(_reportService.GetSummaryByFindingType());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting summary");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Returns the full audit history for a single finding by its SourceRecordId.</summary>
    [HttpGet("history/{sourceRecordId}")]
    public IActionResult GetHistory(string sourceRecordId)
    {
        try
        {
            return Ok(_reportService.GetHistoryBySourceRecordId(sourceRecordId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting history for SourceRecordId {SourceRecordId}", sourceRecordId);
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