using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

/// <summary>
/// Owner-scoped CRUD over recorded third-party services.
///
/// Uniqueness is per owner, matching the database index: two teams that both call
/// Stripe each record their own row.
/// </summary>
public sealed class ExternalServiceService(
    AuditDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IExternalServiceService
{
    public async Task<IReadOnlyList<ExternalServiceDto>> ListAsync(
        string? search, CancellationToken cancellationToken = default)
    {
        var ownerId = OwnerId();
        var query = context.ExternalServices.AsNoTracking().Where(service => service.OwnerUserId == ownerId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(service =>
                EF.Functions.ILike(service.Name, $"%{needle}%") ||
                EF.Functions.ILike(service.Kind, $"%{needle}%") ||
                (service.Provider != null && EF.Functions.ILike(service.Provider, $"%{needle}%")));
        }

        var services = await query.OrderBy(service => service.Name).ToListAsync(cancellationToken);
        return await WithCountsAsync(services, ownerId, cancellationToken);
    }

    public async Task<ExternalServiceDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = OwnerId();
        var service = await context.ExternalServices.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.OwnerUserId == ownerId, cancellationToken);
        if (service is null) return null;

        var counted = await WithCountsAsync([service], ownerId, cancellationToken);
        return counted[0];
    }

    public async Task<ExternalServiceDto?> CreateAsync(
        CreateExternalServiceDto request, CancellationToken cancellationToken = default)
    {
        var ownerId = OwnerId();
        var name = request.Name.Trim();

        var duplicate = await context.ExternalServices
            .AnyAsync(service => service.OwnerUserId == ownerId && service.Name == name, cancellationToken);
        if (duplicate) return null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new ExternalService
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = name,
            Kind = request.Kind.Trim(),
            Endpoint = Normalise(request.Endpoint),
            Provider = Normalise(request.Provider),
            Protocol = Normalise(request.Protocol),
            Port = request.Port,
            Environment = request.Environment.Trim(),
            Notes = Normalise(request.Notes),
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.ExternalServices.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return ToDto(entity, dependentApplicationCount: 0);
    }

    public async Task<ExternalServiceUpdateResult> UpdateAsync(
        Guid id, UpdateExternalServiceDto request, CancellationToken cancellationToken = default)
    {
        var ownerId = OwnerId();
        var entity = await context.ExternalServices
            .FirstOrDefaultAsync(service => service.Id == id && service.OwnerUserId == ownerId, cancellationToken);
        if (entity is null) return ExternalServiceUpdateResult.NotFound;

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            var taken = await context.ExternalServices.AnyAsync(
                service => service.OwnerUserId == ownerId && service.Name == name && service.Id != id,
                cancellationToken);
            if (taken) return ExternalServiceUpdateResult.NameConflict;
            entity.Name = name;
        }

        if (!string.IsNullOrWhiteSpace(request.Kind)) entity.Kind = request.Kind.Trim();
        if (!string.IsNullOrWhiteSpace(request.Environment)) entity.Environment = request.Environment.Trim();
        if (request.Endpoint is not null) entity.Endpoint = Normalise(request.Endpoint);
        if (request.Provider is not null) entity.Provider = Normalise(request.Provider);
        if (request.Protocol is not null) entity.Protocol = Normalise(request.Protocol);
        if (request.Notes is not null) entity.Notes = Normalise(request.Notes);
        if (request.Port is not null) entity.Port = request.Port;

        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ExternalServiceUpdateResult.Updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = OwnerId();
        var entity = await context.ExternalServices
            .FirstOrDefaultAsync(service => service.Id == id && service.OwnerUserId == ownerId, cancellationToken);
        if (entity is null) return false;

        // Dependencies referencing this service cascade away with it, so deleting an
        // external service cannot leave dangling edges on the dependency graph.
        context.ExternalServices.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private string OwnerId() =>
        currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated owner is required.");

    /// <summary>
    /// Counts distinct source applications per service in one query rather than per row.
    /// </summary>
    private async Task<List<ExternalServiceDto>> WithCountsAsync(
        List<ExternalService> services, string ownerId, CancellationToken cancellationToken)
    {
        if (services.Count == 0) return [];

        var ids = services.Select(service => service.Id).ToArray();
        var counts = await context.AppDependencies.AsNoTracking()
            .Where(dependency => dependency.OwnerUserId == ownerId &&
                                 dependency.ExternalServiceId != null &&
                                 ids.Contains(dependency.ExternalServiceId.Value))
            .GroupBy(dependency => dependency.ExternalServiceId!.Value)
            .Select(group => new { ServiceId = group.Key, Count = group.Select(item => item.SourceAppId).Distinct().Count() })
            .ToDictionaryAsync(item => item.ServiceId, item => item.Count, cancellationToken);

        return services
            .Select(service => ToDto(service, counts.GetValueOrDefault(service.Id)))
            .ToList();
    }

    private static ExternalServiceDto ToDto(ExternalService service, int dependentApplicationCount) => new()
    {
        Id = service.Id,
        Name = service.Name,
        Kind = service.Kind,
        Endpoint = service.Endpoint,
        Provider = service.Provider,
        Protocol = service.Protocol,
        Port = service.Port,
        Environment = service.Environment,
        Notes = service.Notes,
        OwnerUserId = service.OwnerUserId ?? string.Empty,
        DependentApplicationCount = dependentApplicationCount,
    };

    /// <summary>Treats an empty string as "not provided" so optional fields stay null.</summary>
    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
