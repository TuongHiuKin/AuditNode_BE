using System.Net;
using System.Text;
using System.Text.Json;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public class KeycloakAuthServiceTests
{
    [Fact]
    public async Task LoginAsync_UsesConfiguredConfidentialClientAndReturnsTokenSet()
    {
        var accessToken = Guid.NewGuid().ToString("N");
        var refreshToken = Guid.NewGuid().ToString("N");
        string? requestBody = null;
        var service = CreateService(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, new
            {
                access_token = accessToken,
                refresh_token = refreshToken,
                expires_in = 300,
                refresh_expires_in = 1800
            });
        });

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Username = "user",
            Password = Guid.NewGuid().ToString("N")
        });

        result.AccessToken.Should().Be(accessToken);
        result.RefreshToken.Should().Be(refreshToken);
        requestBody.Should().Contain("grant_type=password");
        requestBody.Should().Contain("client_id=configured-bff");
        requestBody.Should().NotContain("audit-frontend");
    }

    [Fact]
    public async Task LoginAsync_InvalidGrant_ThrowsAuthenticationFailureWithoutRawBody()
    {
        var service = CreateService(_ => Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, new { error = "invalid_grant" })));

        var action = () => service.LoginAsync(new LoginRequestDto
        {
            Username = "user",
            Password = Guid.NewGuid().ToString("N")
        });

        var exception = await action.Should().ThrowAsync<IdentityAuthenticationException>();
        exception.Which.Message.Should().NotContain("invalid_grant");
    }

    [Fact]
    public async Task LoginAsync_MissingConfiguration_FailsBeforeSendingHttp()
    {
        var calls = 0;
        var service = CreateService(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, includeBffClient: false);

        var action = () => service.LoginAsync(new LoginRequestDto());

        await action.Should().ThrowAsync<IdentityConfigurationException>();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_NetworkFailure_ThrowsUpstreamUnavailable()
    {
        var service = CreateService(_ => throw new HttpRequestException("synthetic network failure"));

        var action = () => service.LoginAsync(new LoginRequestDto());

        await action.Should().ThrowAsync<IdentityUpstreamUnavailableException>();
    }

    [Fact]
    public async Task RegisterAsync_CreatesEnabledUserWithNonTemporaryPasswordAndNoRoles()
    {
        var accessToken = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var call = 0;
        string? createBody = null;
        var service = CreateService(async request =>
        {
            call++;
            if (call == 1)
            {
                return JsonResponse(HttpStatusCode.OK, new { access_token = accessToken, expires_in = 60 });
            }

            if (call is 2 or 3)
            {
                return JsonResponse(HttpStatusCode.OK, Array.Empty<object>());
            }

            createBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        await service.RegisterAsync(new RegisterRequestDto
        {
            Username = "user",
            Email = "user@example.test",
            Password = password
        });

        call.Should().Be(4);
        using var document = JsonDocument.Parse(createBody!);
        var root = document.RootElement;
        root.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var credential = root.GetProperty("credentials")[0];
        credential.GetProperty("temporary").GetBoolean().Should().BeFalse();
        credential.GetProperty("value").GetString().Should().Be(password);
        root.TryGetProperty("realmRoles", out _).Should().BeFalse();
        root.TryGetProperty("clientRoles", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_ExistingUsername_ThrowsConflictWithoutCreatingUser()
    {
        var call = 0;
        var service = CreateService(_ =>
        {
            call++;
            return Task.FromResult(call == 1
                ? JsonResponse(HttpStatusCode.OK, new { access_token = Guid.NewGuid().ToString("N"), expires_in = 60 })
                : JsonResponse(HttpStatusCode.OK, new[] { new { id = Guid.NewGuid().ToString("N") } }));
        });

        var action = () => service.RegisterAsync(new RegisterRequestDto
        {
            Username = "user",
            Email = "user@example.test",
            Password = Guid.NewGuid().ToString("N")
        });

        await action.Should().ThrowAsync<IdentityConflictException>();
        call.Should().Be(2);
    }

    [Fact]
    public async Task RefreshAsync_UsesRefreshGrantAndReturnsRotatedTokenSet()
    {
        var accessToken = Guid.NewGuid().ToString("N");
        var rotatedRefreshToken = Guid.NewGuid().ToString("N");
        string? requestBody = null;
        var service = CreateService(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, new
            {
                access_token = accessToken,
                refresh_token = rotatedRefreshToken,
                expires_in = 300,
                refresh_expires_in = 1800
            });
        });

        var result = await service.RefreshAsync(Guid.NewGuid().ToString("N"));

        result.RefreshToken.Should().Be(rotatedRefreshToken);
        requestBody.Should().Contain("grant_type=refresh_token");
        requestBody.Should().Contain("client_id=configured-bff");
    }

    private static KeycloakAuthService CreateService(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder,
        bool includeBffClient = true)
    {
        var handler = new StubHandler(responder);
        var client = new HttpClient(handler);
        var factory = new Mock<IKeycloakHttpClientFactory>();
        factory.Setup(instance => instance.CreateClient()).Returns(client);

        var values = new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = "https://identity.example.test/realms/test-realm",
            ["Keycloak:Realm"] = "test-realm",
            ["Keycloak:AdminClientId"] = "configured-admin",
            ["Keycloak:AdminClientSecret"] = Guid.NewGuid().ToString("N")
        };

        if (includeBffClient)
        {
            values["Keycloak:BffClientId"] = "configured-bff";
            values["Keycloak:BffClientSecret"] = Guid.NewGuid().ToString("N");
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var mutationLock = new Mock<ISystemAdminMutationLock>();
        mutationLock.Setup(x => x.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> action, CancellationToken cancellationToken) => action(cancellationToken));
        return new KeycloakAuthService(factory.Object, configuration, mutationLock.Object,
            NullLogger<KeycloakAuthService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }
}
