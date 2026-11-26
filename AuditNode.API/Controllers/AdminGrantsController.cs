using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

/// <summary>
/// Lets a SystemAdmin manage sharing on behalf of a user.
///
/// The administrator manages only Viewer/Editor grants — who may see which label. They
/// cannot create, edit or delete the user's catalog rows; that stays under
/// <see cref="AdminCatalogController"/>, which is read-only.
/// </summary>
[Authorize(Policy = "SystemAdminOnly")]
[ApiController]
[Route("api/v1/admin/users/{ownerUserId}/grants")]
public sealed class AdminGrantsController(ILabelGrantService grants) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LabelGrantDto>>> List(
        string ownerUserId, [FromQuery] Guid labelId, CancellationToken cancellationToken)
    {
        if (labelId == Guid.Empty) return BadRequest(Problem(400, "A labelId is required."));
        var result = await grants.ListAsync(labelId, ownerUserId, cancellationToken);
        return result is null ? NotFound(Denied()) : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<LabelGrantDto>> Create(
        string ownerUserId,
        [FromQuery] Guid labelId,
        [FromBody] CreateLabelGrantDto request,
        CancellationToken cancellationToken)
    {
        if (labelId == Guid.Empty) return BadRequest(Problem(400, "A labelId is required."));
        var result = await grants.CreateAsync(labelId, request, ownerUserId, cancellationToken);
        return result.Status switch
        {
            LabelGrantMutationStatus.Success when result.Grant is not null =>
                StatusCode(StatusCodes.Status201Created, result.Grant),
            LabelGrantMutationStatus.Denied => NotFound(Denied()),
            LabelGrantMutationStatus.Invalid => BadRequest(Problem(400, "The grant request is invalid.")),
            LabelGrantMutationStatus.Conflict => Conflict(Problem(409, "The grant was changed by another request.")),
            _ => StatusCode(500, Problem(500, "The grant operation could not be completed.")),
        };
    }

    [HttpPut("{grantId:guid}")]
    public async Task<ActionResult<LabelGrantDto>> Update(
        string ownerUserId,
        Guid grantId,
        [FromQuery] Guid labelId,
        [FromBody] UpdateLabelGrantDto request,
        CancellationToken cancellationToken)
    {
        if (labelId == Guid.Empty) return BadRequest(Problem(400, "A labelId is required."));
        var result = await grants.UpdateAsync(labelId, grantId, request, ownerUserId, cancellationToken);
        return result.Status switch
        {
            LabelGrantMutationStatus.Success when result.Grant is not null => Ok(result.Grant),
            LabelGrantMutationStatus.Denied => NotFound(Denied()),
            LabelGrantMutationStatus.Invalid => BadRequest(Problem(400, "The grant request is invalid.")),
            LabelGrantMutationStatus.Conflict => Conflict(Problem(409, "The grant was changed by another request.")),
            _ => StatusCode(500, Problem(500, "The grant operation could not be completed.")),
        };
    }

    [HttpDelete("{grantId:guid}")]
    public async Task<IActionResult> Revoke(
        string ownerUserId,
        Guid grantId,
        [FromQuery] Guid labelId,
        [FromQuery] long? version,
        CancellationToken cancellationToken)
    {
        if (labelId == Guid.Empty) return BadRequest(Problem(400, "A labelId is required."));
        if (!version.HasValue || version.Value < 1)
            return BadRequest(Problem(400, "A positive grant version is required."));

        var result = await grants.RevokeAsync(labelId, grantId, version.Value, ownerUserId, cancellationToken);
        return result.Status switch
        {
            LabelGrantMutationStatus.Success => NoContent(),
            LabelGrantMutationStatus.Denied => NotFound(Denied()),
            LabelGrantMutationStatus.Invalid => BadRequest(Problem(400, "The grant request is invalid.")),
            LabelGrantMutationStatus.Conflict => Conflict(Problem(409, "The grant was changed by another request.")),
            _ => StatusCode(500, Problem(500, "The grant operation could not be completed.")),
        };
    }

    private static ProblemDetails Denied() => Problem(404, "The label was not found or is unavailable.");
    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
}
