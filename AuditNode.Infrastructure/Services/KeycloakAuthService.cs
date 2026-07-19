using AuditNode.Application.DTOs.Auth;
using AuditNode.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AuditNode.Infrastructure.Services;

public class KeycloakAuthService : IAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakAuthService> _logger;

    public KeycloakAuthService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<KeycloakAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request)
    {
        var settings = GetSettings();
        var tokenEndpoint = settings.TokenEndpoint;
        
        var client = _httpClientFactory.CreateClient();
        
        var requestParams = new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("client_id", settings.ClientId),
            new KeyValuePair<string, string>("client_secret", settings.ClientSecret),
            new KeyValuePair<string, string>("username", request.Username),
            new KeyValuePair<string, string>("password", request.Password)
        };

        _logger.LogInformation("Login requested for {Username}.", request.Username);

        var content = new FormUrlEncodedContent(requestParams);

        var response = await client.PostAsync(tokenEndpoint, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("[DEBUG LOGIN ERROR FROM KEYCLOAK]: Status: {StatusCode}, Content: {ErrorContent}", response.StatusCode, errorContent);
            
            string errorMessage = "Authentication failed.";
            if (errorContent.Contains("invalid_grant"))
            {
                errorMessage = "Invalid username or password.";
            }
            else if (errorContent.Contains("unauthorized_client"))
            {
                errorMessage = "Invalid client credentials configuration.";
            }
            else
            {
                errorMessage = $"Authentication error: {response.StatusCode}";
            }

            throw new UnauthorizedAccessException(errorMessage);
        }

        var jsonStr = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<KeycloakTokenResponse>(jsonStr);

        return new AuthResponseDto
        {
            AccessToken = tokenResponse?.AccessToken ?? string.Empty,
            RefreshToken = tokenResponse?.RefreshToken ?? string.Empty,
            ExpiresIn = tokenResponse?.ExpiresIn ?? 0
        };
    }

    public async Task<bool> RegisterAsync(RegisterRequestDto request)
    {
        var settings = GetSettings();

        // Step 1: Obtain Service Account token
        var tokenEndpoint = settings.TokenEndpoint;
        var client = _httpClientFactory.CreateClient();
        
        var tokenContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", settings.ClientId),
            new KeyValuePair<string, string>("client_secret", settings.ClientSecret)
        });

        var tokenResponseTask = await client.PostAsync(tokenEndpoint, tokenContent);
        if (!tokenResponseTask.IsSuccessStatusCode)
        {
            var error = await tokenResponseTask.Content.ReadAsStringAsync();
            throw new Exception($"Failed to obtain service account token: {error}");
        }
        
        var tokenJsonStr = await tokenResponseTask.Content.ReadAsStringAsync();
        var serviceAccountToken = JsonSerializer.Deserialize<KeycloakTokenResponse>(tokenJsonStr);

        // Step 2: Create User
        var adminEndpoint = $"{settings.ServerBaseUrl}/admin/realms/{settings.Realm}/users";
        
        var userPayload = new
        {
            username = request.Username,
            email = request.Email,
            firstName = "AuditNode",
            lastName = request.Username,
            enabled = true,
            emailVerified = true,
            credentials = new[]
            {
                new
                {
                    type = "password",
                    value = request.Password,
                    temporary = false
                }
            }
        };

        _logger.LogInformation("Registration requested for {Username}.", request.Username);

        var rawJsonPayload = JsonSerializer.Serialize(userPayload);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, adminEndpoint)
        {
            Content = new StringContent(rawJsonPayload, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceAccountToken?.AccessToken);

        var createUserResponse = await client.SendAsync(requestMessage);

        if (createUserResponse.IsSuccessStatusCode)
        {
            return true;
        }

        var errorStr = await createUserResponse.Content.ReadAsStringAsync();
        throw new Exception($"Registration failed: {errorStr}");
    }

    private class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private KeycloakSettings GetSettings()
    {
        var authority = RequiredConfiguration("Keycloak:Authority").TrimEnd('/');
        var authorityUri = new Uri(authority, UriKind.Absolute);

        return new KeycloakSettings(
            authority,
            authorityUri.GetLeftPart(UriPartial.Authority),
            RequiredConfiguration("Keycloak:Realm"),
            RequiredConfiguration("Keycloak:ClientId"),
            RequiredConfiguration("Keycloak:ClientSecret"));
    }

    private string RequiredConfiguration(string key) =>
        _configuration[key] ?? throw new InvalidOperationException($"Missing required configuration value: {key}.");

    private sealed record KeycloakSettings(
        string Authority,
        string ServerBaseUrl,
        string Realm,
        string ClientId,
        string ClientSecret)
    {
        public string TokenEndpoint => $"{Authority}/protocol/openid-connect/token";
    }
}
