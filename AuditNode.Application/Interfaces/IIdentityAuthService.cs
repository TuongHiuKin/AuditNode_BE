using System.Security.Claims;
using AuditNode.Application.DTOs;

namespace AuditNode.Application.Interfaces;

public interface IIdentityAuthService
{
    Task<IdentityTokenSet> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<IdentityTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    CurrentUserDto GetCurrentUser(ClaimsPrincipal principal);
}
