using AuditNode.Domain.Entities;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IApplicationRepository
{
    Task<IEnumerable<ApplicationResponseDto>> GetApplicationsAsync();
    Task<Domain.Entities.Application> CreateApplicationAsync(Domain.Entities.Application application);
}
