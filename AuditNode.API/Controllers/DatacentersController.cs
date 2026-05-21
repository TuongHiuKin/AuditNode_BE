using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AuditNode.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatacentersController : ControllerBase
{
    private readonly IDatacenterRepository _datacenterRepository;

    public DatacentersController(IDatacenterRepository datacenterRepository)
    {
        _datacenterRepository = datacenterRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Datacenter>>> GetDatacenters()
    {
        var datacenters = await _datacenterRepository.GetAllDatacentersAsync();
        return Ok(datacenters);
    }

    [HttpPost]
    public async Task<ActionResult<Datacenter>> CreateDatacenter(CreateDatacenterDto dto)
    {
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Location = dto.Location
        };

        var result = await _datacenterRepository.CreateDatacenterAsync(datacenter);
        return CreatedAtAction(nameof(GetDatacenters), new { id = result.Id }, result);
    }
}
