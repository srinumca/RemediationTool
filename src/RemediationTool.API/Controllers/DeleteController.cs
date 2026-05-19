using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Services;

[ApiController]
[Route("api/delete")]
public class DeleteController : ControllerBase
{
    private readonly DeleteService _service;
    private readonly ILogger<DeleteController> _logger;

    public DeleteController(DeleteService service, ILogger<DeleteController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _service.DeleteAsync(id);
            return Ok($"Deleted {id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {Id}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("all")]
    public async Task<IActionResult> DeleteAll()
    {
        try
        {
            await _service.DeleteAllAsync();
            return Ok("All quarantined files deleted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all files");
            return StatusCode(500, "Internal server error");
        }
    }
}