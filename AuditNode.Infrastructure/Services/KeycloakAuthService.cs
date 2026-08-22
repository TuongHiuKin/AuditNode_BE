using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace AuditNode.Infrastructure.Services;

public sealed class KeycloakAuthService : IIdentityAuthService
{
    public const string HttpClientName = "Keycloak";

    private readonly IKeycloakHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public KeycloakAuthService(IKeycloakHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<IdentityTokenSet> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = Required("Keycloak:BffClientId"),
            ["client_secret"] = Required("Keycloak:BffClientSecret"),
            ["username"] = request.Username,
            ["password"] = request.Password
        };

        return await ExchangeTokensAsync(values, cancellationToken);
    }

    public async Task RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var adminToken = await GetAdminTokenAsync(cancellationToken);
        var usersEndpoint = UsersEndpoint();

        if (await UserExistsAsync(usersEndpoint, "username", request.Username, adminToken, cancellationToken) ||
            await UserExistsAsync(usersEndpoint, "email", request.Email, adminToken, cancellationToken))
        {
            throw new IdentityConflictException();
        }

        var payload = JsonSerializer.Serialize(new
        {
            username = request.Username,
            email = request.Email,
            enabled = true,
            credentials = new[]
            {
                new { type = "password", value = request.Password, temporary = false }
            }
        });

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, usersEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await SendAsync(createRequest, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new IdentityConflictException();
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new IdentityConfigurationException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new IdentityUpstreamUnavailableException();
        }
    }

    public async Task<IdentityTokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = Required("Keycloak:BffClientId"),
            ["client_secret"] = Required("Keycloak:BffClientSecret"),
            ["refresh_token"] = refreshToken
        };

        return await ExchangeTokensAsync(values, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = Required("Keycloak:BffClientId"),
            ["client_secret"] = Required("Keycloak:BffClientSecret"),
            ["refresh_token"] = refreshToken
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Authority()}/protocol/openid-connect/logout")
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new IdentityAuthenticationException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new IdentityUpstreamUnavailableException();
        }
    }

    public CurrentUserDto GetCurrentUser(ClaimsPrincipal principal)
    {
        var roles = principal.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new CurrentUserDto
        {
            Id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value
                ?? string.Empty,
            Username = principal.FindFirst("preferred_username")?.Value
                ?? principal.Identity?.Name
                ?? string.Empty,
            Email = principal.FindFirst(ClaimTypes.Email)?.Value
                ?? principal.FindFirst("email")?.Value,
            Roles = roles
        };
    }

    private async Task<IdentityTokenSet> ExchangeTokensAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint())
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowTokenFailureAsync(response, cancellationToken);
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var token = await JsonSerializer.DeserializeAsync<KeycloakTokenResponse>(stream, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new IdentityUpstreamUnavailableException();
            }

            return new IdentityTokenSet(
                token.AccessToken,
                token.RefreshToken,
                token.ExpiresIn,
                token.RefreshExpiresIn);
        }
        catch (JsonException)
        {
            throw new IdentityUpstreamUnavailableException();
        }
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Required("Keycloak:AdminClientId"),
            ["client_secret"] = Required("Keycloak:AdminClientSecret")
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint())
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new IdentityConfigurationException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new IdentityUpstreamUnavailableException();
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var token = await JsonSerializer.DeserializeAsync<KeycloakTokenResponse>(stream, cancellationToken: cancellationToken);
            return !string.IsNullOrWhiteSpace(token?.AccessToken)
                ? token.AccessToken
                : throw new IdentityUpstreamUnavailableException();
        }
        catch (JsonException)
        {
            throw new IdentityUpstreamUnavailableException();
        }
    }

    private async Task<bool> UserExistsAsync(
        string endpoint,
        string field,
        string value,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var url = $"{endpoint}?{field}={Uri.EscapeDataString(value)}&exact=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new IdentityConfigurationException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new IdentityUpstreamUnavailableException();
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            throw new IdentityUpstreamUnavailableException();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new IdentityUpstreamUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IdentityUpstreamUnavailableException();
        }
    }

    private static async Task ThrowTokenFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.TryGetProperty("error", out var error) &&
                    string.Equals(error.GetString(), "invalid_grant", StringComparison.Ordinal))
                {
                    throw new IdentityAuthenticationException();
                }
            }
            catch (JsonException)
            {
                // The upstream response is intentionally not surfaced.
            }

            throw new IdentityConfigurationException();
        }

        throw new IdentityUpstreamUnavailableException();
    }

    private string TokenEndpoint() => $"{Authority()}/protocol/openid-connect/token";

    private string UsersEndpoint()
    {
        var authority = Authority();
        var markerIndex = authority.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            throw new IdentityConfigurationException();
        }

        var serverBase = authority[..markerIndex].TrimEnd('/');
        return $"{serverBase}/admin/realms/{Uri.EscapeDataString(Required("Keycloak:Realm"))}/users";
    }

    private string Authority() => Required("Keycloak:Authority").TrimEnd('/');

    private string Required(string key)
    {
        var value = _configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new IdentityConfigurationException();
    }

    private sealed class KeycloakTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_expires_in")]
        public int RefreshExpiresIn { get; init; }
    }
}

public interface IKeycloakHttpClientFactory
{
    HttpClient CreateClient();
}

public sealed class KeycloakRuntimeOptions
{
    public string Authority { get; init; } = string.Empty;
    public string Realm { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string AdminClientId { get; init; } = string.Empty;
    public string AdminClientSecret { get; init; } = string.Empty;
    public string BffClientId { get; init; } = string.Empty;
    public string BffClientSecret { get; init; } = string.Empty;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(Realm) &&
        !string.IsNullOrWhiteSpace(Audience) &&
        !string.IsNullOrWhiteSpace(AdminClientId) &&
        !string.IsNullOrWhiteSpace(AdminClientSecret) &&
        !string.IsNullOrWhiteSpace(BffClientId) &&
        !string.IsNullOrWhiteSpace(BffClientSecret);
}
