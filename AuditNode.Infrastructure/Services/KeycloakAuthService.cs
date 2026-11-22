using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Services;

public sealed class KeycloakAuthService : IIdentityAuthService, IIdentityAdminService
{
    public const string HttpClientName = "Keycloak";
    private static readonly TimeSpan PostMutationVerificationTimeout = TimeSpan.FromSeconds(10);

    private readonly IKeycloakHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ISystemAdminMutationLock _systemAdminMutationLock;
    private readonly ILogger<KeycloakAuthService> _logger;

    public KeycloakAuthService(
        IKeycloakHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ISystemAdminMutationLock systemAdminMutationLock,
        ILogger<KeycloakAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _systemAdminMutationLock = systemAdminMutationLock;
        _logger = logger;
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
        EnsureIdentityIsMutable(userId);
        if (enabled)
        {
            var token = await GetAdminTokenAsync(cancellationToken);
            await SetUserEnabledCoreAsync(userId, true, token, cancellationToken);
            return;
        }

        await _systemAdminMutationLock.ExecuteAsync(async lockToken =>
        {
            var token = await GetAdminTokenAsync(lockToken);
            var targetWasEnabled = await GetUserEnabledAsync(userId, token, lockToken);
            if (!targetWasEnabled) return;
            var before = await GetSystemAdminsAsync(token, lockToken);
            EnsureCanRemoveEnabledAdmin(userId, before);
            var mutationDispatched = false;
            try
            {
                mutationDispatched = true;
                await SetUserEnabledCoreAsync(userId, false, token, lockToken);
            }
            catch (OperationCanceledException) when (mutationDispatched && lockToken.IsCancellationRequested)
            {
                await VerifyDisableOrRecoverAsync(userId, token);
                throw;
            }
            catch (Exception exception) when (mutationDispatched && IsUncertainMutationFailure(exception))
            {
                await VerifyDisableOrRecoverAsync(userId, token);
                _logger.LogWarning(exception,
                    "Identity mutation transport failed but desired state and invariant were verified. TargetUserId={TargetUserId} Action={Action} MutationOutcome={MutationOutcome}",
                    userId, "disable", "AppliedAndVerifiedAfterTransportFailure");
                throw;
            }
            await VerifyDisableOrRecoverAsync(userId, token);
        }, cancellationToken);
    }

    public Task CreateUserAsync(CreateIdentityAdminUserDto request, CancellationToken cancellationToken = default) =>
        RegisterAsync(new RegisterRequestDto { Username = request.Username, Email = request.Email, Password = request.Password }, cancellationToken);

    public async Task SetSystemAdminAsync(string userId, bool enabled, CancellationToken cancellationToken = default)
    {
        EnsureIdentityIsMutable(userId);
        if (enabled)
        {
            var token = await GetAdminTokenAsync(cancellationToken);
            var roleJson = await GetSystemAdminRoleJsonAsync(token, cancellationToken);
            await SetSystemAdminRoleCoreAsync(userId, true, roleJson, token, cancellationToken);
            return;
        }

        await _systemAdminMutationLock.ExecuteAsync(async lockToken =>
        {
            var token = await GetAdminTokenAsync(lockToken);
            var roleJson = await GetSystemAdminRoleJsonAsync(token, lockToken);
            var before = await GetSystemAdminsAsync(token, lockToken);
            var targetWasAdmin = before.Any(x => x.Id == userId);
            if (!targetWasAdmin) return;
            EnsureCanRemoveEnabledAdmin(userId, before);
            var mutationDispatched = false;
            try
            {
                mutationDispatched = true;
                await SetSystemAdminRoleCoreAsync(userId, false, roleJson, token, lockToken);
            }
            catch (OperationCanceledException) when (mutationDispatched && lockToken.IsCancellationRequested)
            {
                await VerifyRoleRevokeOrRecoverAsync(userId, roleJson, token);
                throw;
            }
            catch (Exception exception) when (mutationDispatched && IsUncertainMutationFailure(exception))
            {
                await VerifyRoleRevokeOrRecoverAsync(userId, roleJson, token);
                _logger.LogWarning(exception,
                    "Identity mutation transport failed but desired state and invariant were verified. TargetUserId={TargetUserId} Action={Action} MutationOutcome={MutationOutcome}",
                    userId, "revoke_system_admin", "AppliedAndVerifiedAfterTransportFailure");
                throw;
            }
            await VerifyRoleRevokeOrRecoverAsync(userId, roleJson, token);
        }, cancellationToken);
    }

    private static void EnsureCanRemoveEnabledAdmin(string userId, IReadOnlyList<SystemAdminState> admins)
    {
        if (admins.Any(x => x.Id == userId && x.Enabled) && admins.Count(x => x.Enabled) <= 1)
            throw new IdentityConflictException();
    }

    private void EnsureIdentityIsMutable(string userId)
    {
        var protectedUserId = _configuration["Keycloak:BreakGlassUserId"];
        if (!string.IsNullOrWhiteSpace(protectedUserId) &&
            string.Equals(protectedUserId, userId, StringComparison.Ordinal))
            throw new IdentityProtectedException();
    }

    private static bool IsUncertainMutationFailure(Exception exception) =>
        exception is IdentityUpstreamUnavailableException or IdentityConfigurationException;

    private async Task<string> GetSystemAdminRoleJsonAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{RealmAdminEndpoint()}/roles/{Uri.EscapeDataString("SystemAdmin")}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? new IdentityConfigurationException()
                : new IdentityUpstreamUnavailableException();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task SetUserEnabledCoreAsync(string userId, bool enabled, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{UsersEndpoint()}/{Uri.EscapeDataString(userId)}")
        { Content = new StringContent(JsonSerializer.Serialize(new { enabled }), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new IdentityConfigurationException()
                : new IdentityUpstreamUnavailableException();
    }

    private async Task SetSystemAdminRoleCoreAsync(string userId, bool enabled, string roleJson, string token, CancellationToken cancellationToken)
    {
        using var mapping = new HttpRequestMessage(enabled ? HttpMethod.Post : HttpMethod.Delete,
            $"{UsersEndpoint()}/{Uri.EscapeDataString(userId)}/role-mappings/realm")
        { Content = new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json") };
        mapping.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(mapping, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? new IdentityConfigurationException()
                : new IdentityUpstreamUnavailableException();
    }

    private async Task VerifyDisableOrRecoverAsync(string userId, string token)
    {
        using var verification = new CancellationTokenSource(PostMutationVerificationTimeout);
        try
        {
            var enabled = await GetUserEnabledAsync(userId, token, verification.Token);
            var admins = await GetSystemAdminsAsync(token, verification.Token);
            if (!enabled && admins.Any(x => x.Enabled)) return;
        }
        catch (Exception exception) when (exception is not IdentityConfigurationException)
        {
            _logger.LogCritical(exception,
                "SystemAdmin invariant verification failed after disabling identity. TargetUserId={TargetUserId} Action={Action}",
                userId, "disable");
        }

        await RecoverEnabledStateAsync(userId, token, "disable");
    }

    private async Task VerifyRoleRevokeOrRecoverAsync(string userId, string roleJson, string token)
    {
        using var verification = new CancellationTokenSource(PostMutationVerificationTimeout);
        try
        {
            var admins = await GetSystemAdminsAsync(token, verification.Token);
            if (admins.All(x => x.Id != userId) && admins.Any(x => x.Enabled)) return;
        }
        catch (Exception exception) when (exception is not IdentityConfigurationException)
        {
            _logger.LogCritical(exception,
                "SystemAdmin invariant verification failed after revoking role. TargetUserId={TargetUserId} Action={Action}",
                userId, "revoke_system_admin");
        }

        await RecoverSystemAdminRoleAsync(userId, roleJson, token);
    }

    private async Task RecoverEnabledStateAsync(string userId, string token, string action)
    {
        using var recovery = new CancellationTokenSource(PostMutationVerificationTimeout);
        try
        {
            await SetUserEnabledCoreAsync(userId, true, token, recovery.Token);
            var admins = await GetSystemAdminsAsync(token, recovery.Token);
            if (admins.Any(x => x.Enabled))
                _logger.LogError("SystemAdmin invariant recovery restored identity. TargetUserId={TargetUserId} Action={Action}", userId, action);
            else
                _logger.LogCritical("SystemAdmin invariant recovery left no enabled administrator. TargetUserId={TargetUserId} Action={Action}", userId, action);
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "SystemAdmin invariant recovery failed. TargetUserId={TargetUserId} Action={Action}", userId, action);
        }
        throw new IdentityInvariantViolationException();
    }

    private async Task RecoverSystemAdminRoleAsync(string userId, string roleJson, string token)
    {
        using var recovery = new CancellationTokenSource(PostMutationVerificationTimeout);
        try
        {
            await SetSystemAdminRoleCoreAsync(userId, true, roleJson, token, recovery.Token);
            var admins = await GetSystemAdminsAsync(token, recovery.Token);
            var roleRestored = admins.Any(x => x.Id == userId);
            var enabledAdminExists = admins.Any(x => x.Enabled);
            if (roleRestored && enabledAdminExists)
                _logger.LogError("SystemAdmin invariant recovery restored prior role and an enabled administrator exists. TargetUserId={TargetUserId} Action={Action} RoleRestored={RoleRestored} EnabledAdminExists={EnabledAdminExists}", userId, "revoke_system_admin", roleRestored, enabledAdminExists);
            else
                _logger.LogCritical("SystemAdmin invariant recovery did not restore a safe state. TargetUserId={TargetUserId} Action={Action} RoleRestored={RoleRestored} EnabledAdminExists={EnabledAdminExists}", userId, "revoke_system_admin", roleRestored, enabledAdminExists);
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "SystemAdmin invariant recovery failed. TargetUserId={TargetUserId} Action={Action}", userId, "revoke_system_admin");
        }
        throw new IdentityInvariantViolationException();
    }

    private async Task<bool> GetUserEnabledAsync(string userId, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{UsersEndpoint()}/{Uri.EscapeDataString(userId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new IdentityUpstreamUnavailableException();
        try
        {
            using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return user.RootElement.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? enabled.GetBoolean()
                : throw new IdentityUpstreamUnavailableException();
        }
        catch (JsonException) { throw new IdentityUpstreamUnavailableException(); }
    }

    private async Task<IReadOnlyList<SystemAdminState>> GetSystemAdminsAsync(string token, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        const int maximumPages = 100;
        var admins = new List<SystemAdminState>();
        for (var page = 0; page < maximumPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{RealmAdminEndpoint()}/roles/SystemAdmin/users?first={page * pageSize}&max={pageSize}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new IdentityUpstreamUnavailableException();
            try
            {
                using var users = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (users.RootElement.ValueKind != JsonValueKind.Array) throw new IdentityUpstreamUnavailableException();
                var count = 0;
                foreach (var user in users.RootElement.EnumerateArray())
                {
                    var id = user.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id) || !user.TryGetProperty("enabled", out var enabled) ||
                        enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new IdentityUpstreamUnavailableException();
                    admins.Add(new SystemAdminState(id, enabled.GetBoolean()));
                    count++;
                }
                if (count < pageSize) return admins;
            }
            catch (JsonException) { throw new IdentityUpstreamUnavailableException(); }
        }
        throw new IdentityUpstreamUnavailableException();
    }

    private async Task<HashSet<string>> GetSystemAdminIdsAsync(string token, CancellationToken cancellationToken)
    {
        var admins = await GetSystemAdminsAsync(token, cancellationToken);
        return admins.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
    }

    private sealed record SystemAdminState(string Id, bool Enabled);

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
