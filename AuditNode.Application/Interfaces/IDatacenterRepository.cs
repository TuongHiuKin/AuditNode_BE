using AuditNode.Domain.Entities;

namespace AuditNode.Application.Interfaces;

public interface IDatacenterRepository
{
    Task<Datacenter> CreateDatacenterAsync(Datacenter datacenter);
    Task<IEnumerable<Datacenter>> GetAllDatacentersAsync();
}
