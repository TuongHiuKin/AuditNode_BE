using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace AuditNode.Infrastructure.Services;

public sealed class KeycloakAuthService : IIdentityAuthService, IIdentityAdminService
{
    private static readonly SemaphoreSlim AdminMutationLock = new(1, 1);
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

    public async Task<IReadOnlyList<IdentityAdminUserDto>> ListUsersAsync(string? search, int first, int max, CancellationToken cancellationToken = default)
    {
        var token = await GetAdminTokenAsync(cancellationToken);
        var url = $"{UsersEndpoint()}?first={Math.Max(0, first)}&max={Math.Clamp(max, 1, 100)}" +
                  (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? new IdentityConfigurationException() : new IdentityUpstreamUnavailableException();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var adminIds = await GetSystemAdminIdsAsync(token, cancellationToken);
            return document.RootElement.EnumerateArray().Select(user => new IdentityAdminUserDto(
                user.GetProperty("id").GetString() ?? string.Empty,
                user.TryGetProperty("username", out var username) ? username.GetString() ?? string.Empty : string.Empty,
                user.TryGetProperty("email", out var email) ? email.GetString() : null,
                !user.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean(),
                IsSystemAdmin: adminIds.Contains(user.GetProperty("id").GetString() ?? string.Empty))).ToList();
        }
        catch (JsonException) { throw new IdentityUpstreamUnavailableException(); }
    }

    public async Task SetEnabledAsync(string userId, bool enabled, CancellationToken cancellationToken = default)
    {
        await AdminMutationLock.WaitAsync(cancellationToken);
        try
        {
        var token = await GetAdminTokenAsync(cancellationToken);
        if (!enabled) await EnsureNotLastSystemAdminAsync(userId, token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{UsersEndpoint()}/{Uri.EscapeDataString(userId)}")
        { Content = new StringContent(JsonSerializer.Serialize(new { enabled }), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? new IdentityConfigurationException() : new IdentityUpstreamUnavailableException();
        }
        finally { AdminMutationLock.Release(); }
    }

    public Task CreateUserAsync(CreateIdentityAdminUserDto request, CancellationToken cancellationToken = default) =>
        RegisterAsync(new RegisterRequestDto { Username = request.Username, Email = request.Email, Password = request.Password }, cancellationToken);

    public async Task SetSystemAdminAsync(string userId, bool enabled, CancellationToken cancellationToken = default)
    {
        await AdminMutationLock.WaitAsync(cancellationToken);
        try
        {
        var token = await GetAdminTokenAsync(cancellationToken);
        var roleUrl = $"{RealmAdminEndpoint()}/roles/{Uri.EscapeDataString("SystemAdmin")}";
        using var roleRequest = new HttpRequestMessage(HttpMethod.Get, roleUrl);
        roleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var roleResponse = await SendAsync(roleRequest, cancellationToken);
        if (!roleResponse.IsSuccessStatusCode) throw roleResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound ? new IdentityConfigurationException() : new IdentityUpstreamUnavailableException();
        var roleJson = await roleResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!enabled) await EnsureNotLastSystemAdminAsync(userId, token, cancellationToken);

        using var mapping = new HttpRequestMessage(enabled ? HttpMethod.Post : HttpMethod.Delete,
            $"{UsersEndpoint()}/{Uri.EscapeDataString(userId)}/role-mappings/realm")
        { Content = new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json") };
        mapping.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var mappingResponse = await SendAsync(mapping, cancellationToken);
        if (!mappingResponse.IsSuccessStatusCode) throw mappingResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound ? new IdentityConfigurationException() : new IdentityUpstreamUnavailableException();
        }
        finally { AdminMutationLock.Release(); }
    }

    private async Task EnsureNotLastSystemAdminAsync(string userId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RealmAdminEndpoint()}/roles/SystemAdmin/users?first=0&max=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new IdentityUpstreamUnavailableException();
        try
        {
            using var users = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (users.RootElement.ValueKind != JsonValueKind.Array) throw new IdentityUpstreamUnavailableException();
            if (users.RootElement.GetArrayLength() <= 1 && users.RootElement.EnumerateArray().Any(x => x.GetProperty("id").GetString() == userId)) throw new IdentityConflictException();
        }
        catch (JsonException) { throw new IdentityUpstreamUnavailableException(); }
    }

    private async Task<HashSet<string>> GetSystemAdminIdsAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RealmAdminEndpoint()}/roles/SystemAdmin/users?first=0&max=1000");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new IdentityUpstreamUnavailableException();
        try
        {
            using var users = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return users.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException) { throw new IdentityUpstreamUnavailableException(); }
    }

    public async Task<IdentityAdminUserDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var token = await GetAdminTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{UsersEndpoint()}/{Uri.EscapeDataString(userId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? new IdentityConfigurationException() : new IdentityUpstreamUnavailableException();
        try
        {
            using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = user.RootElement;
            return new IdentityAdminUserDto(root.GetProperty("id").GetString() ?? string.Empty,
                root.TryGetProperty("username", out var username) ? username.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("email", out var email) ? email.GetString() : null,
                !root.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean());
        }
        catch (JsonException) { throw new IdentityUpstreamUnavailableException(); }
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

    private string RealmAdminEndpoint()
    {
        var users = UsersEndpoint();
        return users[..users.LastIndexOf("/users", StringComparison.Ordinal)];
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
