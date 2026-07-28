using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemediationTool.API.Authorization;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;

namespace RemediationTool.API.Controllers;

/// <summary>
/// Provides configured source systems for upload selection controls.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.InternalApplication)]
[ApiController]
[Route("api/source-systems")]
public sealed class SourceSystemsController : ControllerBase
{
    private readonly ISourceSystemRepository _sourceSystemRepository;

    public SourceSystemsController(ISourceSystemRepository sourceSystemRepository)
    {
        _sourceSystemRepository = sourceSystemRepository;
    }

    /// <summary>
    /// Returns enabled source systems from the source-system configuration table.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SourceSystemOption>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<SourceSystemOption>>> GetEnabled(
        CancellationToken cancellationToken)
    {
        var sourceSystems = await _sourceSystemRepository.GetEnabledAsync(cancellationToken);

        var response = sourceSystems
            .Where(item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.SourceSystem))
            .Select(item => new SourceSystemOption
            {
                SourceSystem = item.SourceSystem,
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                    ? item.SourceSystem
                    : item.DisplayName,
                DataSystem = item.DataSystem,
                OriginatingDataSystem = item.OriginatingDataSystem
            })
            .ToArray();

        return Ok(response);
    }
}
