using Microsoft.AspNetCore.Mvc;
using RemediationTool.Application.Services;

[ApiController]
[Route("api/ingestion")]
public class IngestionController : ControllerBase
{
    private readonly IngestionService _service;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(IngestionService service, ILogger<IngestionController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("File missing");

            var count = await _service.ProcessAsync(file);

            return Ok(new
            {
                message = "Processed successfully",
                records = count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            return StatusCode(500, "Internal server error");
        }
    }
}