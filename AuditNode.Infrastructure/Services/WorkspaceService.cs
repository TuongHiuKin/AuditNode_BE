using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly AuditDbContext _context;

    public WorkspaceService(AuditDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<WorkspaceDto>> GetUserWorkspacesAsync(string userId)
    {
        var workspaces = await _context.Workspaces.ToListAsync();
        
        return workspaces.Select(w => new WorkspaceDto
        {
            Id = w.Id,
            Name = w.Name,
            Description = w.Description
        });
    }
}
