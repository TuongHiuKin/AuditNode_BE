namespace AuditNode.Application.Interfaces;
public interface IWorkspaceUserSummaryService
{
    Task<IReadOnlyDictionary<string, int>> GetWorkspaceCountsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}
