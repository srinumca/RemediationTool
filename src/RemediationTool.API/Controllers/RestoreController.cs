using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Services;

[Authorize(Policy = AuthorizationPolicies.AdminAccess)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Restore(Guid id)
    {
        _logger.LogInformation("[RESTORE REQUEST] FileId: {Id}", id);

        await _service.RestoreAsync(id);

        _logger.LogInformation("[RESTORE RESPONSE] FileId: {Id} — restore triggered.", id);
        return Ok($"Restore triggered for {id}");
    }

    [HttpPost("all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RestoreAll()
    {
        _logger.LogInformation("[RESTORE ALL REQUEST]");

        await _service.RestoreAllAsync();

        _logger.LogInformation("[RESTORE ALL RESPONSE] — completed.");
        return Ok("Restore completed for all files");
    }
}
