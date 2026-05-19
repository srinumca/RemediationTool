using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Services;

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

    [HttpPost("{id}")]
    public async Task<IActionResult> Restore(Guid id)
    {
        try
        {
            await _service.RestoreAsync(id);
            return Ok($"Restore triggered for {id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering restore for {FileId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

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