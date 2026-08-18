using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IInfrastructureService
{
    Task<int> GetDependenciesCountAsync(Guid appId);
    Task<DeploymentOperationStatus> MigrateAppAsync(MigrateAppDto migrateDto);
    Task<bool> PurgeAppAsync(Guid appId);
    Task<IEnumerable<DeployedAppDto>> GetDeployedAppsByServerAsync(Guid serverId);
}
