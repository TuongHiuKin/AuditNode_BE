using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IDatacenterService
{
    Task<IEnumerable<DatacenterDto>> GetDatacentersAsync();
    Task<CursorPageDto<DatacenterDto>> GetCatalogPageAsync(CatalogPageQuery query, CancellationToken cancellationToken = default);
    Task<DatacenterDto> CreateDatacenterAsync(CreateDatacenterDto dto);
}
