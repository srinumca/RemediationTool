
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
        try
        {
            await _service.ProcessAsync();
            return Ok("Quarantine completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running quarantine process");
            return StatusCode(500, "Internal server error");
        }
    }
}
