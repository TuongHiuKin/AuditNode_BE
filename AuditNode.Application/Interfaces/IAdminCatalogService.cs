using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

/// <summary>
/// Read-only inspection of another owner's catalog, for SystemAdmin support work.
///
/// This interface intentionally exposes no write operation. Administrators may look
/// at what a user recorded; changing it stays the owner's job.
/// </summary>
public interface IAdminCatalogService
{
    /// <summary>
    /// Returns <c>null</c> when the owner has no catalog or does not exist.
    /// </summary>
    Task<AdminOwnerCatalogDto?> GetOwnerCatalogAsync(string ownerUserId, CancellationToken cancellationToken = default);
}
