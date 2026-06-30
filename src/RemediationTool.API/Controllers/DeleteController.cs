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
        _logger.LogInformation("[DELETE REQUEST] FileId: {Id}", id);

        await _service.DeleteAsync(id);

        _logger.LogInformation("[DELETE RESPONSE] FileId: {Id} — deleted.", id);
        return Ok($"Deleted {id}");
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    [HttpPost("all")]
    public async Task<IActionResult> DeleteAll()
    {
        _logger.LogInformation("[DELETE ALL REQUEST]");

        await _service.DeleteAllAsync();

        _logger.LogInformation("[DELETE ALL RESPONSE] — completed.");
        return Ok("All quarantined files deleted");
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }
}