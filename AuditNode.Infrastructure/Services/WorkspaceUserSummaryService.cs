using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.Infrastructure.Services;
public sealed class WorkspaceUserSummaryService(AuditDbContext context) : IWorkspaceUserSummaryService
{
    public async Task<IReadOnlyDictionary<string, int>> GetWorkspaceCountsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var owners = await context.Workspaces.AsNoTracking().Where(x => ids.Contains(x.OwnerUserId)).GroupBy(x => x.OwnerUserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var members = await context.WorkspaceMembers.AsNoTracking().Where(x => ids.Contains(x.UserId)).GroupBy(x => x.UserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        return ids.ToDictionary(id => id, id => owners.GetValueOrDefault(id) + members.GetValueOrDefault(id));
    }
}
