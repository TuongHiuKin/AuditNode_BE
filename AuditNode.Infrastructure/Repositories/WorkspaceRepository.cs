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
            .Include(workspace => workspace.Members.Where(member => member.UserId == userId))
                .ThenInclude(member => member.Scopes)
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

    public async Task<Workspace> EnsurePersonalAsync(string userId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(workspace => workspace.OwnerUserId == userId && workspace.IsPersonal, cancellationToken);
        if (existing is not null) return existing;

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Personal Workspace",
            OwnerUserId = userId,
            IsPersonal = true
        };
        _context.Workspaces.Add(workspace);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return workspace;
        }
        catch (DbUpdateException)
        {
            _context.Entry(workspace).State = EntityState.Detached;
            var concurrentlyCreated = await _context.Workspaces.AsNoTracking().SingleOrDefaultAsync(
                item => item.OwnerUserId == userId && item.IsPersonal, cancellationToken);
            if (concurrentlyCreated is not null) return concurrentlyCreated;
            throw;
        }
    }
}
