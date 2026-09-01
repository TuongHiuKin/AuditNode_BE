using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/labels/{labelId:guid}/grants")]
public sealed class LabelGrantsController(
    ILabelGrantService grants,
    ILabelShareOptionsService shareOptions) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LabelGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LabelGrantDto>>> List(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var result = await grants.ListAsync(labelId, cancellationToken);
        return result is null ? NotFound(DeniedError()) : Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LabelGrantDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LabelGrantDto>> Create(
        Guid labelId,
        [FromBody] CreateLabelGrantDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await grants.CreateAsync(labelId, request, cancellationToken);
        return result.Status switch
        {
            LabelGrantMutationStatus.Success when result.Grant is not null =>
                StatusCode(StatusCodes.Status201Created, result.Grant),
            LabelGrantMutationStatus.Denied => NotFound(DeniedError()),
            LabelGrantMutationStatus.Invalid => BadRequest(InvalidError()),
            LabelGrantMutationStatus.Conflict => Conflict(ConflictError()),
            _ => StatusCode(StatusCodes.Status500InternalServerError, FailureError())
        };
    }

    [HttpPut("{grantId:guid}")]
    [ProducesResponseType(typeof(LabelGrantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LabelGrantDto>> Update(
        Guid labelId,
        Guid grantId,
        [FromBody] UpdateLabelGrantDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await grants.UpdateAsync(labelId, grantId, request, cancellationToken);
        return result.Status switch
        {
            LabelGrantMutationStatus.Success when result.Grant is not null => Ok(result.Grant),
            LabelGrantMutationStatus.Denied => NotFound(DeniedError()),
            LabelGrantMutationStatus.Invalid => BadRequest(InvalidError()),
            LabelGrantMutationStatus.Conflict => Conflict(ConflictError()),
            _ => StatusCode(StatusCodes.Status500InternalServerError, FailureError())
        };
    }

    [HttpDelete("{grantId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(
        Guid labelId,
        Guid grantId,
        [FromQuery] long? version,
        CancellationToken cancellationToken = default)
    {
        if (!version.HasValue || version.Value < 1)
            return BadRequest(new { error = "A positive grant version is required." });

        var result = await grants.RevokeAsync(labelId, grantId, version.Value, cancellationToken);
        return result.Status switch
        {
            LabelGrantMutationStatus.Success => NoContent(),
            LabelGrantMutationStatus.Denied => NotFound(DeniedError()),
            LabelGrantMutationStatus.Invalid => BadRequest(InvalidError()),
            LabelGrantMutationStatus.Conflict => Conflict(ConflictError()),
            _ => StatusCode(StatusCodes.Status500InternalServerError, FailureError())
        };
    }

    [HttpGet("~/api/v1/labels/{labelId:guid}/share-options")]
    [EnableRateLimiting("share-options")]
    [ProducesResponseType(typeof(LabelShareOptionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LabelShareOptionsDto>> Options(
        Guid labelId,
        [FromQuery] string? search,
        [FromQuery] int first = 0,
        [FromQuery] int max = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearch = search?.Trim();
        if (normalizedSearch is null || normalizedSearch.Length is < 3 or > 100 ||
            first is < 0 or > 100 || max is < 1 or > 20)
            return BadRequest(new
            {
                error = "Search must contain between 3 and 100 characters; first and max cannot exceed 100 and 20 respectively."
            });

        var result = await shareOptions.GetAsync(
            labelId, normalizedSearch, first, max, cancellationToken);
        return result is null ? NotFound(DeniedError()) : Ok(result);
    }

    private static object DeniedError() => new { error = "The label was not found or is unavailable." };
    private static object InvalidError() => new { error = "The grant request is invalid." };
    private static object ConflictError() => new { error = "The grant was changed by another request." };
    private static object FailureError() => new { error = "The grant operation could not be completed." };
}
