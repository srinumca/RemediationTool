
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RemediationTool.Infrastructure;
using RemediationTool.Application.Interfaces;

namespace RemediationTool.API.Controllers
{
    [ApiController]
    [Route("api/ingestion")]
    public class IngestionController : ControllerBase
    {
        private readonly IStorageService _storage;
        private readonly ILogger<IngestionController> _logger;

        public IngestionController(IStorageService storage, ILogger<IngestionController> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("File missing");

                var key = $"input/{file.FileName}";

                await _storage.UploadAsync(key, file.OpenReadStream());

                return Ok(new { message = "Uploaded", key });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
