using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IWorkspaceRepository
{
    Task<IEnumerable<Workspace>> GetAllAsync();
}
