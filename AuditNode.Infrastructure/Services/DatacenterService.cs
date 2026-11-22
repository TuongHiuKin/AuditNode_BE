using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class DatacenterService : IDatacenterService
{
    private readonly AuditDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IGlobalCatalogRepository _catalog;
    private readonly IOwnerLabelService _ownerLabels;
    private readonly TimeProvider _timeProvider;

    public DatacenterService(AuditDbContext context, ICurrentUserService currentUser, IGlobalCatalogRepository catalog, IOwnerLabelService ownerLabels, TimeProvider timeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _catalog = catalog;
        _ownerLabels = ownerLabels;
        _timeProvider = timeProvider;
    }

    public Task<CursorPageDto<DatacenterDto>> GetCatalogPageAsync(CatalogPageQuery query, string? ownerUserId = null, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? Task.FromResult(new CursorPageDto<DatacenterDto>([], null, false))
            : _catalog.GetDatacentersAsync(_currentUser.UserId!, query, _timeProvider.GetUtcNow().UtcDateTime, ownerUserId, cancellationToken);

    public async Task<IEnumerable<DatacenterDto>> GetDatacentersAsync() =>
        (await GetCatalogPageAsync(new CatalogPageQuery(CatalogView.Mine, 100))).Items;

    public async Task<DatacenterDto> CreateDatacenterAsync(CreateDatacenterDto dto)
    {
        var actor = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException();
        await _ownerLabels.EnsureAsync(actor);
        var datacenter = new Datacenter
        {
            Id = Guid.NewGuid(),
            OwnerUserId = actor,
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
