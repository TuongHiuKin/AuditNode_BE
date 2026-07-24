using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AuditNode.API.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Username and password are required." });
        }

        var authority = _config["Keycloak:Authority"];
        var clientId = "audit-frontend";
        
        if (string.IsNullOrEmpty(authority)) 
        {
            return StatusCode(500, new { error = "Keycloak:Authority is not configured in appsettings." });
        }

        var tokenEndpoint = $"{authority.TrimEnd('/')}/protocol/openid-connect/token";

        using var httpClient = new HttpClient();
        
        var requestParams = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("username", request.Username),
            new KeyValuePair<string, string>("password", request.Password)
        };

        var content = new FormUrlEncodedContent(requestParams);

        try
        {
            var response = await httpClient.PostAsync(tokenEndpoint, content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var tokenData = System.Text.Json.JsonDocument.Parse(responseString);
                var token = tokenData.RootElement.GetProperty("access_token").GetString();
                return Ok(new { accessToken = token });
            }
            
            return Unauthorized(new { 
                error = "Keycloak Error", 
                message = $"Keycloak failed with: {responseString}" 
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(503, new { 
                error = "Connection Error", 
                message = $"Could not connect to Keycloak: {ex.Message}" 
            });
        }
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
