using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AuditNode.API.Controllers;

[Authorize(Roles = "Admin,User")]
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

    [HttpPost]
    public async Task<ActionResult<DatacenterDto>> CreateDatacenter(CreateDatacenterDto dto)
    {
        var result = await _datacenterService.CreateDatacenterAsync(dto);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteDatacenter(Guid id)
    {
        // Implementation for deleting datacenter...
        return Ok(new { message = "Datacenter deleted successfully" });
    }
}
