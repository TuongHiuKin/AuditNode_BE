using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditNode.API.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/inventory/labels")]
public class LabelsController : ControllerBase
{
    private readonly AuditDbContext _context;
    private readonly IScopedResourcePolicy _policy;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenant;

    public LabelsController(AuditDbContext context, IScopedResourcePolicy policy, ICurrentUserService currentUser, ITenantProvider tenant)
    {
        _context = context;
        _policy = policy;
        _currentUser = currentUser;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LabelDto>>> GetLabels()
    {
        if (!_tenant.WorkspaceId.HasValue || string.IsNullOrWhiteSpace(_currentUser.UserId)) return Forbid();
        var servers = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "server");
        var applications = await _policy.GetReadableIdsAsync(_tenant.WorkspaceId.Value, _currentUser.UserId!, "application");
        var query = _context.Labels.AsQueryable();
        if (servers is not null || applications is not null)
            query = query.Where(label =>
                (servers != null && label.ServerLabels.Any(link => servers.Contains(link.ServerId))) ||
                (applications != null && label.ApplicationLabels.Any(link => applications.Contains(link.ApplicationId))));
        var labels = await query
            .Select(l => new LabelDto
            {
                Key = l.Key,
                Value = l.Value
            })
            .Distinct()
            .ToListAsync();

        return Ok(labels);
    }
}
