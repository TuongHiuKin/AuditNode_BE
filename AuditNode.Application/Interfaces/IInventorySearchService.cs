using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IInventorySearchService
{
    Task<IEnumerable<SearchResultDto>> SearchAsync(string? keyword);
}
