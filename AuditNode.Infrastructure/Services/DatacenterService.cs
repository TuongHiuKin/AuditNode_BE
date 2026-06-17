using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class DatacenterService : IDatacenterService
{
    private readonly AuditDbContext _context;

    public DatacenterService(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<DatacenterDto>> GetDatacentersAsync()
    {
        var datacenters = await _context.Datacenters.ToListAsync();
        return datacenters.Select(d => new DatacenterDto
        {
            Id = d.Id,
            Name = d.Name
        });
    }

    public async Task<DatacenterDto> CreateDatacenterAsync(CreateDatacenterDto dto)
    {
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Location = dto.Location
        };

        _context.Datacenters.Add(datacenter);
        await _context.SaveChangesAsync();

        return new DatacenterDto
        {
            Id = datacenter.Id,
            Name = datacenter.Name
        };
    }
}
