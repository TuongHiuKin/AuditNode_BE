using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class DatacenterService : IDatacenterService
{
    private readonly AuditDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DatacenterService(AuditDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<IEnumerable<DatacenterDto>> GetDatacentersAsync()
    {
        var currentUserId = _currentUserService.UserId ?? string.Empty;
        var datacenters = await _context.Datacenters
            .Where(d => d.OwnerId == currentUserId)
            .ToListAsync();
        return datacenters.Select(d => new DatacenterDto
        {
            Id = d.Id,
            Name = d.Name,
            Location = d.Location
        });
    }

    public async Task<DatacenterDto> CreateDatacenterAsync(CreateDatacenterDto dto)
    {
        var currentUserId = _currentUserService.UserId ?? string.Empty;

        var existingDatacenter = await _context.Datacenters
            .FirstOrDefaultAsync(d => d.OwnerId == currentUserId && d.Name.ToLower() == dto.Name.ToLower());

        if (existingDatacenter != null)
        {
            throw new InvalidOperationException($"A Datacenter with the name '{dto.Name}' already exists.");
        }

        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Location = dto.Location,
            OwnerId = currentUserId
        };

        _context.Datacenters.Add(datacenter);
        await _context.SaveChangesAsync();

        return new DatacenterDto
        {
            Id = datacenter.Id,
            Name = datacenter.Name,
            Location = datacenter.Location
        };
    }
}
