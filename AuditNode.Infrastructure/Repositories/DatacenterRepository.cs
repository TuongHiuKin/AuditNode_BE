using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Repositories;

public class DatacenterRepository : IDatacenterRepository
{
    private readonly AuditDbContext _context;

    public DatacenterRepository(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<Datacenter> CreateDatacenterAsync(Datacenter datacenter)
    {
        _context.Datacenters.Add(datacenter);
        await _context.SaveChangesAsync();
        return datacenter;
    }

    public async Task<IEnumerable<Datacenter>> GetAllDatacentersAsync()
    {
        return await _context.Datacenters.AsNoTracking().ToListAsync();
    }
}
