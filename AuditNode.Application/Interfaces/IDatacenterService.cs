using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IDatacenterService
{
    Task<IEnumerable<DatacenterDto>> GetDatacentersAsync();
    Task<DatacenterDto> CreateDatacenterAsync(CreateDatacenterDto dto);
}
