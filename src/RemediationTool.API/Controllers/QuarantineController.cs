using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Models;
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

    /// <summary>
    /// Queues selected NotYetStarted obsolete records for quarantine.
    /// Only an Admin or System Admin may initiate this action.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminAccess)]
    [HttpPost("queue")]
    [ProducesResponseType(typeof(QuarantineBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Queue([FromBody] QuarantineRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequest("Quarantine request is required.");

        if (!request.IncludeAllEligibleNotYetStarted && request.RecordIds.Count == 0)
            return BadRequest("At least one RecordId is required unless IncludeAllEligibleNotYetStarted is true.");

        _logger.LogInformation(
            "[QUARANTINE_QUEUE_REQUEST] RequestedBy:{RequestedBy}, RecordIds:{RecordIdsCount}, IncludeAllEligible:{IncludeAllEligible}, ProcessImmediately:{ProcessImmediately}",
            request.RequestedBy, request.RecordIds.Count, request.IncludeAllEligibleNotYetStarted, request.ProcessImmediately);

        var response = await _service.QueueAsync(request, cancellationToken);

        _logger.LogInformation(
            "[QUARANTINE_QUEUE_RESPONSE] RunId:{RunId}, Queued:{Queued}, Processed:{Processed}, Succeeded:{Succeeded}, Failed:{Failed}, Skipped:{Skipped}",
            response.RunId, response.QueuedCount, response.ProcessedCount, response.SucceededCount, response.FailedCount, response.SkippedCount);

        return Ok(response);
    }

    /// <summary>
    /// Processes all records currently in PendingQuarantine.
    /// Intended for a background job or Step Function and requires an app token.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.InternalApplication)]
    [HttpPost("run")]
    [ProducesResponseType(typeof(QuarantineBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[QUARANTINE_RUN_REQUEST] Processing all PendingQuarantine records.");

        var response = await _service.ProcessAsync(cancellationToken);

        _logger.LogInformation(
            "[QUARANTINE_RUN_RESPONSE] RunId:{RunId}, Processed:{Processed}, Succeeded:{Succeeded}, Failed:{Failed}, Skipped:{Skipped}",
            response.RunId, response.ProcessedCount, response.SucceededCount, response.FailedCount, response.SkippedCount);

        return Ok(response);
    }
}
