using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1")]
public sealed class ShareLinksController(IShareTokenService shareTokens, IShareCatalogService shareCatalog) : ControllerBase
{
    [HttpGet("labels/{labelId:guid}/share-links")]
    [ProducesResponseType(typeof(IReadOnlyList<ShareLinkMetadataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ShareLinkMetadataDto>>> List(
        Guid labelId,
        CancellationToken cancellationToken = default)
    {
        var result = await shareTokens.ListAsync(labelId, cancellationToken);
        return result is null ? NotFound(DeniedError()) : Ok(result);
    }

    [HttpPost("labels/{labelId:guid}/share-links")]
    [EnableRateLimiting("share-link-create")]
    [ProducesResponseType(typeof(CreateShareLinkResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CreateShareLinkResponseDto>> Create(
        Guid labelId,
        [FromBody] CreateShareLinkDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await shareTokens.CreateAsync(labelId, request.ExpiresAt, cancellationToken);
        return result.Status switch
        {
            ShareTokenMutationStatus.Success when
                result.GrantId.HasValue &&
                result.RawToken is not null &&
                result.ExpiresAt.HasValue &&
                result.Version.HasValue =>
                StatusCode(StatusCodes.Status201Created, new CreateShareLinkResponseDto(
                    result.GrantId.Value,
                    result.RawToken,
                    result.ExpiresAt.Value,
                    result.Version.Value,
                    result.SharesAllOwnerResources,
                    result.WarningCode)),
            ShareTokenMutationStatus.Denied => NotFound(DeniedError()),
            ShareTokenMutationStatus.Invalid => BadRequest(InvalidError()),
            ShareTokenMutationStatus.Conflict => Conflict(ConflictError()),
            _ => StatusCode(StatusCodes.Status500InternalServerError, FailureError())
        };
    }

    [HttpDelete("labels/{labelId:guid}/share-links/{grantId:guid}")]
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
            return BadRequest(new { error = "A positive share-link version is required." });

        var result = await shareTokens.RevokeAsync(labelId, grantId, version.Value, cancellationToken);
        return result.Status switch
        {
            ShareTokenMutationStatus.Success => NoContent(),
            ShareTokenMutationStatus.Denied => NotFound(DeniedError()),
            ShareTokenMutationStatus.Invalid => BadRequest(InvalidError()),
            ShareTokenMutationStatus.Conflict => Conflict(ConflictError()),
            _ => StatusCode(StatusCodes.Status500InternalServerError, FailureError())
        };
    }

    [AllowAnonymous]
    [HttpPost("share-links/resolve")]
    [EnableRateLimiting("share-link-resolve")]
    [ProducesResponseType(typeof(ShareTokenResolutionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ShareTokenResolutionDto>> Resolve(
        [FromBody] ResolveShareLinkDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await shareTokens.ResolveAsync(request.Token, cancellationToken);
        return result is null ? NotFound(ResolveDeniedError()) : Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("share-links/browse")]
    [EnableRateLimiting("share-link-browse")]
    [ProducesResponseType(typeof(CursorPageDto<ShareCatalogItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CursorPageDto<ShareCatalogItemDto>>> Browse(
        [FromBody] BrowseShareLinkDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await shareCatalog.BrowseAsync(request, cancellationToken);
            return result is null ? NotFound(ResolveDeniedError()) : Ok(result);
        }
        catch (AuditNode.Application.Exceptions.CatalogQueryValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private static object DeniedError() => new { error = "The label was not found or is unavailable." };
    private static object ResolveDeniedError() => new { error = "The share link is invalid or unavailable." };
    private static object InvalidError() => new { error = "The share-link request is invalid." };
    private static object ConflictError() => new { error = "The share link was changed by another request." };
    private static object FailureError() => new { error = "The share-link operation could not be completed." };
}
