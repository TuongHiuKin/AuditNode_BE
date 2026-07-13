using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using AppEntity = AuditNode.Domain.Entities.Application;

namespace AuditNode.Infrastructure.Services;

public class ApplicationService : IApplicationService
{
    private readonly AuditDbContext _context;

    public ApplicationService(AuditDbContext context)
    {
        _context = context;
    }

    private async Task<List<Label>> ProcessLabelsAsync(IEnumerable<LabelDto> incomingLabels)
    {
        var processedLabels = new List<Label>();
        if (incomingLabels == null || !incomingLabels.Any()) return processedLabels;

        foreach (var labelDto in incomingLabels)
        {
            var existingLabel = await _context.Labels
                .FirstOrDefaultAsync(l => l.Key == labelDto.Key && l.Value == labelDto.Value);

            if (existingLabel != null)
            {
                processedLabels.Add(existingLabel);
            }
            else
            {
                var newLabel = new Label 
                { 
                    Id = Guid.NewGuid(), 
                    Key = labelDto.Key, 
                    Value = labelDto.Value, 
                    ColorHex = labelDto.ColorHex ?? string.Empty 
                };
                processedLabels.Add(newLabel);
            }
        }
        return processedLabels;
    }

    public async Task<ApplicationResponseDto> CreateAsync(CreateApplicationDto appDto)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existingApp = await _context.Applications
                .FirstOrDefaultAsync(a => a.AppCode == appDto.AppCode.ToUpper());

            AppEntity application;

            if (existingApp == null)
            {
                application = new AppEntity
                {
                    Id = Guid.NewGuid(),
                    AppCode = appDto.AppCode.ToUpper(),
                    AppName = appDto.AppName,
                    OwnerTeam = appDto.OwnerTeam,
                    Risk = string.IsNullOrWhiteSpace(appDto.Risk) ? "LOW" : appDto.Risk,
                    Icon = appDto.Icon ?? string.Empty,
                    TechStack = appDto.TechStack ?? string.Empty,
                    Labels = await ProcessLabelsAsync(appDto.Labels)
                };
                _context.Applications.Add(application);
            }
            else
            {
                existingApp.AppName = appDto.AppName;
                existingApp.OwnerTeam = appDto.OwnerTeam;
                existingApp.Risk = string.IsNullOrWhiteSpace(appDto.Risk) ? "LOW" : appDto.Risk;
                existingApp.Icon = appDto.Icon ?? string.Empty;
                existingApp.TechStack = appDto.TechStack ?? string.Empty;
                existingApp.Labels = await ProcessLabelsAsync(appDto.Labels);
                application = existingApp;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetByIdAsync(application.Id) ?? new ApplicationResponseDto();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateApplicationDto updateDto)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var app = await _context.Applications
                .Include(a => a.Labels)
                .FirstOrDefaultAsync(a => a.Id == id);
                
            if (app == null) return false;

            app.AppName = updateDto.AppName;
            app.OwnerTeam = updateDto.OwnerTeam;
            app.Risk = updateDto.Risk;
            app.Icon = updateDto.Icon;
            app.TechStack = updateDto.TechStack;

            var incomingLabels = updateDto.Labels ?? new List<LabelDto>();

            // 1. Identify labels to REMOVE
            var labelsToRemove = app.Labels
                .Where(sl => !incomingLabels.Any(il => il.Key == sl.Key && il.Value == sl.Value))
                .ToList();

            foreach (var label in labelsToRemove)
            {
                app.Labels.Remove(label);
            }

            // 2. Identify labels to ADD
            var labelsToAddDtos = incomingLabels
                .Where(il => !app.Labels.Any(sl => sl.Key == il.Key && sl.Value == il.Value))
                .ToList();

            var labelsToAdd = await ProcessLabelsAsync(labelsToAddDtos);

            foreach (var label in labelsToAdd)
            {
                app.Labels.Add(label);
            }

            // Also handle PortMapping logic as before
            var portMapping = await _context.PortMappings.FirstOrDefaultAsync(p => p.AppId == id);
            
            if (updateDto.TargetServerId.HasValue || updateDto.PortNumber.HasValue)
            {
                if (portMapping == null)
                {
                    portMapping = new PortMapping
                    {
                        Id = Guid.NewGuid(),
                        AppId = id,
                        ServerId = updateDto.TargetServerId ?? Guid.Empty,
                        PortNumber = updateDto.PortNumber ?? 0,
                        Protocol = "TCP"
                    };
                    _context.PortMappings.Add(portMapping);
                }
                else
                {
                    if (updateDto.TargetServerId.HasValue && updateDto.TargetServerId.Value != Guid.Empty)
                        portMapping.ServerId = updateDto.TargetServerId.Value;

                    if (updateDto.PortNumber.HasValue)
                        portMapping.PortNumber = updateDto.PortNumber.Value;

                    _context.PortMappings.Update(portMapping);
                }
            }

            await _context.SaveChangesAsync();
            
            // Orphaned labels cleanup
            if (labelsToRemove.Any())
            {
                foreach(var rl in labelsToRemove)
                {
                    bool isUsedByServer = await _context.Servers.AnyAsync(s => s.Labels.Any(l => l.Id == rl.Id));
                    bool isUsedByApp = await _context.Applications.AnyAsync(a => a.Labels.Any(l => l.Id == rl.Id));
                    if (!isUsedByServer && !isUsedByApp)
                    {
                        _context.Labels.Remove(rl);
                    }
                }
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetAllAsync(string[]? labels = null)
    {
        var query = _context.Applications
            .Include(a => a.Labels)
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .AsSplitQuery()
            .AsQueryable();

        if (labels != null && labels.Length > 0)
        {
            query = query.Where(a => a.Labels.Any(l => labels.Contains(l.Key) || labels.Contains(l.Value)));
        }

        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var query = _context.Applications
            .Include(a => a.Labels)
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .Where(a => ids.Contains(a.Id))
            .AsSplitQuery();

        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<ApplicationResponseDto?> GetByIdAsync(Guid id)
    {
        var app = await _context.Applications
            .Include(a => a.Labels)
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app == null) return null;

        return new ApplicationResponseDto
        {
            Id = app.Id,
            AppCode = app.AppCode,
            AppName = app.AppName,
            OwnerTeam = app.OwnerTeam,
            Risk = app.Risk,
            Icon = app.Icon,
            TechStack = app.TechStack,
            Labels = app.Labels?.Select(l => new LabelDto { Key = l.Key, Value = l.Value, ColorHex = l.ColorHex }).ToList() ?? new List<LabelDto>(),
            Servers = app.PortMappings.Select(pm => new ServerOnApplicationDto
            {
                Id = pm.ServerId,
                Hostname = pm.Server?.Hostname ?? string.Empty,
                IpAddress = pm.Server?.IpAddress ?? string.Empty,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList()
        };
    }

    private IQueryable<ApplicationResponseDto> MapToResponseDto(IQueryable<AppEntity> query)
    {
        return query.Select(a => new ApplicationResponseDto
        {
            Id = a.Id,
            AppCode = a.AppCode,
            AppName = a.AppName,
            OwnerTeam = a.OwnerTeam,
            Risk = a.Risk,
            Icon = a.Icon,
            TechStack = a.TechStack,
            Labels = a.Labels.Select(l => new LabelDto { Key = l.Key, Value = l.Value, ColorHex = l.ColorHex }).ToList(),
            Servers = a.PortMappings.Select(pm => new ServerOnApplicationDto
            {
                Id = pm.ServerId,
                Hostname = pm.Server!.Hostname,
                IpAddress = pm.Server!.IpAddress,
                PortNumber = pm.PortNumber,
                Protocol = pm.Protocol
            }).ToList()
        });
    }
}
