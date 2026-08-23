namespace AuditNode.Application.Interfaces;

public sealed record ShareOptionUserDto(string Id, string Username, string? Email);
public sealed record ShareOptionTargetDto(Guid Id, string DisplayName);
public sealed record WorkspaceShareOptionsDto(IReadOnlyList<ShareOptionUserDto> Users, IReadOnlyList<ShareOptionTargetDto> Labels, IReadOnlyList<ShareOptionTargetDto> Frames);
public interface IWorkspaceShareOptionsService
{
    Task<WorkspaceShareOptionsDto?> GetAsync(Guid workspaceId, string actorUserId, string? search, int first, int max, CancellationToken cancellationToken = default);
}
