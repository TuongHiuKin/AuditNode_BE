using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface ITopologyCommandService
{
    Task<TopologyCommandResult> ExecuteAsync(TopologyCommandBatchDto batch, CancellationToken cancellationToken = default);
}
