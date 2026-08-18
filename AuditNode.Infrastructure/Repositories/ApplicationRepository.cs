using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly AuditDbContext _dbContext;

    public ApplicationRepository(AuditDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetApplicationsAsync(
        string? labelKey = null,
        string? labelValue = null)
    {
        var query = ReadQuery();
        if (!string.IsNullOrWhiteSpace(labelKey))
        {
            query = query.Where(application => application.ApplicationLabels.Any(link =>
                link.Label != null &&
                link.Label.Key == labelKey &&
                (string.IsNullOrWhiteSpace(labelValue) || link.Label.Value == labelValue)));
        }

        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var requestedIds = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        return await MapToResponseDto(ReadQuery().Where(application => requestedIds.Contains(application.Id)))
            .ToListAsync();
    }

    public Task<AppEntity?> GetByIdAsync(Guid id) => ReadQuery().FirstOrDefaultAsync(application => application.Id == id);

    public Task<bool> AppCodeExistsAsync(string appCode, Guid? excludeApplicationId = null) =>
        _dbContext.Applications.AnyAsync(application =>
            application.AppCode == appCode &&
            (!excludeApplicationId.HasValue || application.Id != excludeApplicationId.Value));

    public Task<bool> ServerExistsAsync(Guid serverId) =>
        _dbContext.Servers.AnyAsync(server => server.Id == serverId);

    public Task<bool> PortCollisionExistsAsync(
        Guid serverId,
        int portNumber,
        Guid? excludePortMappingId = null) =>
        _dbContext.PortMappings.AnyAsync(mapping =>
            mapping.ServerId == serverId &&
            mapping.PortNumber == portNumber &&
            (!excludePortMappingId.HasValue || mapping.Id != excludePortMappingId.Value));

    public Task<PortMapping?> GetPortMappingAsync(Guid portMappingId) =>
        _dbContext.PortMappings.FirstOrDefaultAsync(mapping => mapping.Id == portMappingId);

    public async Task<AppEntity> CreateAsync(
        AppEntity application,
        IReadOnlyCollection<LabelDto> labels,
        PortMapping? deployment)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        _dbContext.Applications.Add(application);
        if (deployment is not null)
            _dbContext.PortMappings.Add(deployment);

        await ReplaceLabelsAsync(application, labels);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return application;
    }

    public async Task UpdateAsync(
        AppEntity application,
        IReadOnlyCollection<LabelDto>? labels,
        PortMapping? deployment)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        if (labels is not null)
            await ReplaceLabelsAsync(application, labels);
        if (deployment is not null)
            _dbContext.PortMappings.Update(deployment);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private IQueryable<AppEntity> ReadQuery() => _dbContext.Applications
        .Include(application => application.PortMappings)
            .ThenInclude(mapping => mapping.Server)
        .Include(application => application.ApplicationLabels)
            .ThenInclude(link => link.Label)
        .AsSplitQuery();

    private async Task ReplaceLabelsAsync(AppEntity application, IReadOnlyCollection<LabelDto> labels)
    {
        var currentLinks = await _dbContext.ApplicationLabels
            .Where(link => link.ApplicationId == application.Id)
            .ToListAsync();
        _dbContext.ApplicationLabels.RemoveRange(currentLinks);
        if (currentLinks.Count > 0)
            await _dbContext.SaveChangesAsync();

        var normalized = labels
            .Select(label => new LabelDto { Key = label.Key.Trim(), Value = label.Value.Trim() })
            .DistinctBy(label => new { label.Key, label.Value })
            .ToArray();

        foreach (var value in normalized)
        {
            var label = await _dbContext.Labels.FirstOrDefaultAsync(existing =>
                existing.Key == value.Key && existing.Value == value.Value);
            if (label is null)
            {
                label = new Label { Id = Guid.NewGuid(), Key = value.Key, Value = value.Value };
                _dbContext.Labels.Add(label);
            }

            _dbContext.ApplicationLabels.Add(new ApplicationLabel
            {
                ApplicationId = application.Id,
                LabelId = label.Id,
                Application = application,
                Label = label
            });
        }
    }

    private static IQueryable<ApplicationResponseDto> MapToResponseDto(IQueryable<AppEntity> query) =>
        query.Select(application => new ApplicationResponseDto
        {
            Id = application.Id,
            AppCode = application.AppCode,
            AppName = application.AppName,
            OwnerTeam = application.OwnerTeam,
            Risk = application.Risk,
            Icon = application.Icon,
            TechStack = application.TechStack,
            Servers = application.PortMappings.Select(mapping => new ServerOnApplicationDto
            {
                PortMappingId = mapping.Id,
                Id = mapping.ServerId,
                Hostname = mapping.Server!.Hostname,
                IpAddress = mapping.Server.IpAddress,
                PortNumber = mapping.PortNumber,
                Protocol = mapping.Protocol
            }).ToList(),
            Labels = application.ApplicationLabels.Select(link => new LabelDto
            {
                Key = link.Label!.Key,
                Value = link.Label.Value
            }).ToList()
        });
}
