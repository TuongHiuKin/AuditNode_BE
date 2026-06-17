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

    public async Task<IEnumerable<ApplicationResponseDto>> GetAllAsync()
    {
        var query = _context.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server);
        
        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<IEnumerable<ApplicationResponseDto>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var query = _context.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
            .Where(a => ids.Contains(a.Id));

        return await MapToResponseDto(query).ToListAsync();
    }

    public async Task<ApplicationResponseDto?> GetByIdAsync(Guid id)
    {
        var app = await _context.Applications
            .Include(a => a.PortMappings)
                .ThenInclude(pm => pm.Server)
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
                    TechStack = appDto.TechStack ?? string.Empty
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
                application = existingApp;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ApplicationResponseDto
            {
                Id = application.Id,
                AppCode = application.AppCode,
                AppName = application.AppName,
                OwnerTeam = application.OwnerTeam,
                Risk = application.Risk,
                Icon = application.Icon,
                TechStack = application.TechStack,
                Servers = new List<ServerOnApplicationDto>()
            };
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
            var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == id);
            if (app == null) return false;

            app.AppName = updateDto.AppName;
            app.OwnerTeam = updateDto.OwnerTeam;
            app.Risk = updateDto.Risk;
            app.Icon = updateDto.Icon;
            app.TechStack = updateDto.TechStack;

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
            await transaction.CommitAsync();
            return true;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
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
