using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Models;
using RemediationTool.Application.Services;
using RemediationTool.Domain.Enum;

namespace RemediationTool.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IngestionService _ingestionService;
    private readonly ILogger<IngestionController> _logger;


    public IngestionController(
        IngestionService ingestionService,
        ILogger<IngestionController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(IngestionUploadResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        var response = await _ingestionService.ProcessAsync(file);

        if (response.Status == IngestionJobStatus.Failed && response.TotalRecords == 0)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}