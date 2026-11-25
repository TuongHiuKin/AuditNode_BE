using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

/// <summary>
/// Third-party services the caller's catalog depends on — payment providers, mail
/// relays, partner APIs. Scoped to the authenticated owner.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/external-services")]
public sealed class ExternalServicesController(IExternalServiceService services) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExternalServiceDto>>> List(
        [FromQuery] string? search, CancellationToken cancellationToken)
        => Ok(await services.ListAsync(search, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExternalServiceDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var service = await services.GetAsync(id, cancellationToken);
        return service is null ? NotFound(Problem(404, "External service was not found.")) : Ok(service);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateExternalServiceDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Kind) ||
            string.IsNullOrWhiteSpace(request.Environment))
            return BadRequest(Problem(400, "Name, kind, and environment are required."));

        if (request.Port is < 1 or > 65535)
            return BadRequest(Problem(400, "Port must be between 1 and 65535."));

        var created = await services.CreateAsync(request, cancellationToken);
        if (created is null)
            return Conflict(Problem(409, "An external service with this name already exists in your catalog."));

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateExternalServiceDto request, CancellationToken cancellationToken)
    {
        if (request.Port is < 1 or > 65535)
            return BadRequest(Problem(400, "Port must be between 1 and 65535."));

        return await services.UpdateAsync(id, request, cancellationToken) switch
        {
            ExternalServiceUpdateResult.Updated => NoContent(),
            ExternalServiceUpdateResult.NotFound => NotFound(Problem(404, "External service was not found.")),
            ExternalServiceUpdateResult.NameConflict => Conflict(Problem(409, "Another external service already uses this name.")),
            _ => StatusCode(500, Problem(500, "The external service could not be updated.")),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => await services.DeleteAsync(id, cancellationToken)
            ? NoContent()
            : NotFound(Problem(404, "External service was not found."));

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
}
