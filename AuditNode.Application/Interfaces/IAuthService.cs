using AuditNode.Application.DTOs.Auth;
using System.Threading.Tasks;

namespace AuditNode.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request);
    Task<bool> RegisterAsync(RegisterRequestDto request);
}
