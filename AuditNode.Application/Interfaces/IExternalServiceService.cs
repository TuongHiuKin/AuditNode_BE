using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

/// <summary>
/// Manages the third-party services an owner's catalog depends on. Every read and
/// write is scoped to the calling owner; a service recorded by one owner is
/// invisible to another.
/// </summary>
public interface IExternalServiceService
{
    Task<IReadOnlyList<ExternalServiceDto>> ListAsync(string? search, CancellationToken cancellationToken = default);
    Task<ExternalServiceDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>null</c> when the owner already recorded a service with this name.</summary>
    Task<ExternalServiceDto?> CreateAsync(CreateExternalServiceDto request, CancellationToken cancellationToken = default);

    Task<ExternalServiceUpdateResult> UpdateAsync(Guid id, UpdateExternalServiceDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum ExternalServiceUpdateResult
{
    Updated,
    NotFound,
    NameConflict,
}
