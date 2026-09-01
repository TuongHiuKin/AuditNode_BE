using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuditNode.API.Security;
using AuditNode.Application.Exceptions;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route(ApiRoutes.BaseRoute)]
public class DatacentersController : ControllerBase
{
    private readonly IDatacenterService _datacenterService;

    public DatacentersController(IDatacenterService datacenterService)
    {
        _datacenterService = datacenterService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CursorPageDto<DatacenterDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPageDto<DatacenterDto>>> GetDatacenters(
        [FromQuery] string? view = null,
        [FromQuery] int? limit = null,
        [FromQuery] string? cursor = null,
        [FromQuery] string? ownerUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _datacenterService.GetCatalogPageAsync(CatalogPageQuery.Parse(view, limit, cursor), ownerUserId, cancellationToken));
        }
        catch (CatalogQueryValidationException exception)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = exception.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<DatacenterDto>> CreateDatacenter(CreateDatacenterDto dto)
    {
        var result = await _datacenterService.CreateDatacenterAsync(dto);
        return Ok(result);
    }
}
