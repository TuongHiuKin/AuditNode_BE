using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Repositories;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly AuditDbContext _context;

    public WorkspaceRepository(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Workspace>> GetAllAsync()
    {
        return await _context.Workspaces.AsNoTracking().ToListAsync();
    }

    public async Task<IEnumerable<Workspace>> GetAccessibleAsync(string userId)
    {
        return await _context.Workspaces
            .AsNoTracking()
            .Where(workspace => workspace.OwnerUserId == userId ||
                workspace.Members.Any(member => member.UserId == userId))
            .OrderBy(workspace => workspace.Name)
            .ToListAsync();
    }

    public Task<bool> ExistsAsync(Guid workspaceId)
    {
        return _context.Workspaces.AsNoTracking().AnyAsync(workspace => workspace.Id == workspaceId);
    }

    public Task<bool> UserHasAccessAsync(Guid workspaceId, string userId)
    {
        return _context.Workspaces.AsNoTracking().AnyAsync(workspace =>
            workspace.Id == workspaceId &&
            (workspace.OwnerUserId == userId || workspace.Members.Any(member => member.UserId == userId)));
    }
}
