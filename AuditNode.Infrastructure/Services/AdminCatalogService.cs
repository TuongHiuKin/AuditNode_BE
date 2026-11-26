using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

/// <summary>
/// Builds a read-only view of one owner's catalog for a SystemAdmin.
///
/// Every query filters on <c>OwnerUserId</c> explicitly rather than going through the
/// caller-scoped authorization helpers: the point is to look at another owner's rows,
/// and the controller already restricted access to SystemAdmin.
/// </summary>
public sealed class AdminCatalogService(AuditDbContext context, UserManager<AppUser> users) : IAdminCatalogService
{
    public async Task<AdminOwnerCatalogDto?> GetOwnerCatalogAsync(
        string ownerUserId, CancellationToken cancellationToken = default)
    {
        var owner = await users.FindByIdAsync(ownerUserId);
        if (owner is null) return null;

        var servers = await context.Servers.IgnoreQueryFilters().AsNoTracking()
            .Where(server => server.OwnerUserId == ownerUserId)
            .OrderBy(server => server.Hostname)
            .Select(server => new AdminCatalogServerDto
            {
                Id = server.Id,
                Hostname = server.Hostname,
                IpAddress = server.IpAddress,
                OsType = server.OsType,
                Environment = server.Environment,
                Status = server.Status,
                Datacenter = server.Datacenter != null ? server.Datacenter.Name : string.Empty,
                Labels = context.ServerLabels.IgnoreQueryFilters()
                    .Where(link => link.ServerId == server.Id && link.OwnerUserId == ownerUserId && link.Label != null)
                    .Select(link => link.Label!.Key + "=" + link.Label.Value)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var applications = await context.Applications.IgnoreQueryFilters().AsNoTracking()
            .Where(application => application.OwnerUserId == ownerUserId)
            .OrderBy(application => application.AppCode)
            .Select(application => new AdminCatalogApplicationDto
            {
                Id = application.Id,
                AppCode = application.AppCode,
                AppName = application.AppName,
                OwnerTeam = application.OwnerTeam,
                TechStack = application.TechStack,
                Risk = application.Risk,
                Ports = context.PortMappings.IgnoreQueryFilters()
                    .Where(mapping => mapping.AppId == application.Id && mapping.OwnerUserId == ownerUserId)
                    .Select(mapping => new AdminCatalogPortDto
                    {
                        Id = mapping.Id,
                        PortNumber = mapping.PortNumber,
                        Protocol = mapping.Protocol,
                        ServerHostname = mapping.Server != null ? mapping.Server.Hostname : string.Empty,
                    })
                    .ToList(),
                Labels = context.ApplicationLabels.IgnoreQueryFilters()
                    .Where(link => link.ApplicationId == application.Id && link.OwnerUserId == ownerUserId && link.Label != null)
                    .Select(link => link.Label!.Key + "=" + link.Label.Value)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var externalServices = await context.ExternalServices.IgnoreQueryFilters().AsNoTracking()
            .Where(service => service.OwnerUserId == ownerUserId)
            .OrderBy(service => service.Name)
            .Select(service => new AdminCatalogExternalServiceDto
            {
                Id = service.Id,
                Name = service.Name,
                Kind = service.Kind,
                Endpoint = service.Endpoint,
                Provider = service.Provider,
                Environment = service.Environment,
            })
            .ToListAsync(cancellationToken);

        var dependencies = await context.AppDependencies.IgnoreQueryFilters().AsNoTracking()
            .Where(dependency => dependency.OwnerUserId == ownerUserId)
            .Select(dependency => new AdminCatalogDependencyDto
            {
                Id = dependency.Id,
                SourceAppCode = dependency.SourceApplication != null ? dependency.SourceApplication.AppCode : string.Empty,
                DestinationAppCode = dependency.DestinationApplication != null
                    ? dependency.DestinationApplication.AppCode
                    : null,
                ExternalServiceName = dependency.ExternalService != null
                    ? dependency.ExternalService.Name
                    : null,
            })
            .ToListAsync(cancellationToken);

        var labels = await context.Labels.IgnoreQueryFilters().AsNoTracking()
            .Where(label => label.OwnerUserId == ownerUserId)
            .OrderBy(label => label.Key).ThenBy(label => label.Value)
            .Select(label => new AdminCatalogLabelDto
            {
                Id = label.Id,
                Key = label.Key,
                Value = label.Value,
                Kind = label.Kind,
                ActiveGrantCount = context.LabelGrants.IgnoreQueryFilters()
                    .Count(grant => grant.LabelId == label.Id && grant.OwnerUserId == ownerUserId && grant.RevokedAt == null),
            })
            .ToListAsync(cancellationToken);

        return new AdminOwnerCatalogDto
        {
            OwnerUserId = ownerUserId,
            Username = owner.UserName,
            Email = owner.Email,
            Servers = servers,
            Applications = applications,
            ExternalServices = externalServices,
            Dependencies = dependencies,
            Labels = labels,
        };
    }
}
