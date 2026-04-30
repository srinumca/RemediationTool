using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Services;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/restore")]
public class RestoreController : ControllerBase
{
    private readonly RestoreService _service;
    private readonly ILogger<RestoreController> _logger;

    public RestoreController(RestoreService service, ILogger<RestoreController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // Restore a specific file
    [HttpPost("{fileName}")]
    public async Task<IActionResult> Restore(string fileName)
    {
        try
        {
            await _service.RestoreAsync(fileName);
            return Ok($"Restore triggered for {fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering restore for {File}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }

    // Restore all quarantined files
    [HttpPost("all")]
    public async Task<IActionResult> RestoreAll()
    {
        try
        {
            await _service.RestoreAllAsync();
            return Ok("Restore completed for all files");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering restore for all files");
            return StatusCode(500, "Internal server error");
        }
    }
}