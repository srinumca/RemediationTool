using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Services;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/quarantine")]
public class QuarantineController : ControllerBase
{
    private readonly QuarantineService _service;
    private readonly ILogger<QuarantineController> _logger;

    public QuarantineController(QuarantineService service, ILogger<QuarantineController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        _logger.LogInformation("[QUARANTINE REQUEST] — quarantine run triggered.");

        await _service.ProcessAsync();

        _logger.LogInformation("[QUARANTINE RESPONSE] — quarantine run completed.");
        return Ok("Quarantine completed");
        // Unexpected exceptions fall through to GlobalExceptionMiddleware.
    }
}