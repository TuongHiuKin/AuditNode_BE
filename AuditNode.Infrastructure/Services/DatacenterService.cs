using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class DatacenterService : IDatacenterService
{
    private readonly AuditDbContext _context;
    private readonly IScopedResourcePolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly TimeProvider _timeProvider;

    public DatacenterService(AuditDbContext context, IScopedResourcePolicy policy, ICurrentUserService currentUser, ITenantProvider tenant, IGlobalCatalogRepository catalog, TimeProvider timeProvider)
    {
        _context = context;
        _policy = policy;
        _currentUser = currentUser;
        _tenant = tenant;
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<DatacenterDto>> GetCatalogPageAsync(CatalogPageQuery query, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<DatacenterDto>([], null, false))
            : _catalog.GetDatacentersAsync(_currentUser.UserId!, query, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<IEnumerable<DatacenterDto>> GetDatacentersAsync()
    {
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return [];
        var serverIds = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "server");
        var query = _context.Datacenters.AsQueryable();
        if (serverIds is not null) query = query.Where(datacenter => datacenter.Servers.Any(server => serverIds.Contains(server.Id)));
        var datacenters = await query.ToListAsync();
        return datacenters.Select(d => new DatacenterDto
        {
            Id = d.Id,
            Name = d.Name,
            Location = d.Location
        });
    }

    public async Task<DatacenterDto> CreateDatacenterAsync(CreateDatacenterDto dto)
    {
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(),
            OwnerUserId = _currentUser.UserId,
            Name = dto.Name,
            Location = dto.Location
        };

        _context.Datacenters.Add(datacenter);
        await _context.SaveChangesAsync();

        return new DatacenterDto
        {
            Id = datacenter.Id,
            Name = datacenter.Name,
            Location = datacenter.Location,
            OwnerUserId = datacenter.OwnerUserId ?? string.Empty,
            EffectivePermission = LabelEffectivePermission.Owner,
            Capabilities = CatalogCapabilities.Owner
        };
    }
}
