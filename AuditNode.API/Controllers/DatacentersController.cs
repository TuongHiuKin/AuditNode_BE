using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    public async Task<ActionResult<IEnumerable<DatacenterDto>>> GetDatacenters()
    {
        var result = await _datacenterService.GetDatacentersAsync();
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<DatacenterDto>> CreateDatacenter(CreateDatacenterDto dto)
    {
        var result = await _datacenterService.CreateDatacenterAsync(dto);
        return Ok(result);
    }
}
