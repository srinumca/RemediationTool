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
        _logger.LogInformation("[RESTORE REQUEST] FileId: {Id}", id);

        await _service.RestoreAsync(id);

        _logger.LogInformation("[RESTORE RESPONSE] FileId: {Id} — restore triggered.", id);
        return Ok($"Restore triggered for {id}");
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }

    [HttpPost("all")]
    public async Task<IActionResult> RestoreAll()
    {
        _logger.LogInformation("[RESTORE ALL REQUEST]");

        await _service.RestoreAllAsync();

        _logger.LogInformation("[RESTORE ALL RESPONSE] — completed.");
        return Ok("Restore completed for all files");
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }
}