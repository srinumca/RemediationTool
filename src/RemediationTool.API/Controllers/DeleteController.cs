using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Models;
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
        _logger.LogInformation("[DELETE REQUEST] FileId: {Id}", id);

        await _service.DeleteAsync(id);

        _logger.LogInformation("[DELETE RESPONSE] FileId: {Id} — deleted.", id);
        return Ok($"Deleted {id}");
        // Existing response shape is kept for backward compatibility.
    }

    [HttpPost("all")]
    public async Task<IActionResult> DeleteAll()
    {
        _logger.LogInformation("[DELETE ALL REQUEST]");

        await _service.DeleteAllAsync();

        _logger.LogInformation("[DELETE ALL RESPONSE] — completed.");
        return Ok("All quarantined files deleted");
        // Existing response shape is kept for backward compatibility.
    }

    [HttpPost("retention/run")]
    public async Task<IActionResult> DeleteRetentionEligible()
    {
        _logger.LogInformation("[DELETE RETENTION REQUEST]");

        var deletedCount = await _service.DeleteRetentionEligibleAsync();

        _logger.LogInformation("[DELETE RETENTION RESPONSE] DeletedCount:{DeletedCount}", deletedCount);
        return Ok(ApiResponse<object>.Ok(
            new { DeletedCount = deletedCount },
            "Retention eligible quarantined files processed for deletion.",
            HttpContext.TraceIdentifier));
    }
}
