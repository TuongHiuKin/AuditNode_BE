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

public sealed class KeycloakSystemAdminInvariantTests
{
    [Fact]
    public async Task Concurrent_role_revocations_leave_one_enabled_system_admin()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        var mutationLock = new SerializedMutationLock();
        var first = Service(state, mutationLock);
        var second = Service(state, mutationLock);

        var results = await Task.WhenAll(
            Capture(() => first.SetSystemAdminAsync("admin-a", false)),
            Capture(() => second.SetSystemAdminAsync("admin-b", false)));

        results.Count(x => x is null).Should().Be(1);
        results.Count(x => x is IdentityConflictException).Should().Be(1);
        state.EnabledSystemAdminCount.Should().Be(1);
        mutationLock.MaximumConcurrentHolders.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_disable_and_revoke_leave_one_enabled_system_admin()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        var mutationLock = new SerializedMutationLock();
        var first = Service(state, mutationLock);
        var second = Service(state, mutationLock);

        var results = await Task.WhenAll(
            Capture(() => first.SetEnabledAsync("admin-a", false)),
            Capture(() => second.SetSystemAdminAsync("admin-b", false)));

        results.Count(x => x is null).Should().Be(1);
        results.Count(x => x is IdentityConflictException).Should().Be(1);
        state.EnabledSystemAdminCount.Should().Be(1);
    }

    [Fact]
    public async Task Disabled_system_admin_does_not_allow_disabling_the_last_enabled_admin()
    {
        var state = new IdentityState(("enabled-admin", true), ("disabled-admin", false));
        var service = Service(state, new SerializedMutationLock());

        var action = () => service.SetEnabledAsync("enabled-admin", false);

        await action.Should().ThrowAsync<IdentityConflictException>();
        state.EnabledSystemAdminCount.Should().Be(1);
    }

    [Fact]
    public async Task Last_enabled_admin_is_protected_even_when_disabled_role_members_fill_the_first_page()
    {
        var admins = Enumerable.Range(0, 100)
            .Select(index => ($"disabled-{index}", false))
            .Append(("enabled-admin", true))
            .ToArray();
        var state = new IdentityState(admins);
        var service = Service(state, new SerializedMutationLock());

        var action = () => service.SetSystemAdminAsync("enabled-admin", false);

        await action.Should().ThrowAsync<IdentityConflictException>();
        state.EnabledSystemAdminCount.Should().Be(1);
        state.SnapshotCount.Should().Be(2);
    }

    [Fact]
    public async Task Lock_failure_stops_mutation_before_contacting_keycloak()
    {
        var factory = new Mock<IKeycloakHttpClientFactory>(MockBehavior.Strict);
        var mutationLock = new Mock<ISystemAdminMutationLock>();
        mutationLock.Setup(x => x.ExecuteAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityMutationLockUnavailableException());
        var service = new KeycloakAuthService(factory.Object, Configuration(), mutationLock.Object,
            NullLogger<KeycloakAuthService>.Instance);

        var action = () => service.SetSystemAdminAsync("admin", false);

        await action.Should().ThrowAsync<IdentityMutationLockUnavailableException>();
        factory.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Configured_break_glass_identity_is_immutable_without_contacting_keycloak(bool statusMutation)
    {
        var factory = new Mock<IKeycloakHttpClientFactory>(MockBehavior.Strict);
        var mutationLock = new Mock<ISystemAdminMutationLock>(MockBehavior.Strict);
        var service = new KeycloakAuthService(factory.Object, Configuration("break-glass"), mutationLock.Object,
            NullLogger<KeycloakAuthService>.Instance);

        Func<Task> action = statusMutation
            ? () => service.SetEnabledAsync("break-glass", false)
            : () => service.SetSystemAdminAsync("break-glass", false);

        await action.Should().ThrowAsync<IdentityProtectedException>();
        factory.VerifyNoOtherCalls();
        mutationLock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Zero_admin_post_check_compensates_role_revoke_and_reports_uncertain_result()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        var service = Service(state, new SerializedMutationLock(), removeAllOnRevoke: true);

        var action = () => service.SetSystemAdminAsync("admin-a", false);

        await action.Should().ThrowAsync<IdentityInvariantViolationException>();
        state.EnabledSystemAdminCount.Should().Be(1);
        state.Contains("admin-a").Should().BeTrue();
    }

    [Fact]
    public async Task Request_cancellation_after_mutation_still_runs_bounded_post_verification()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        using var cancellation = new CancellationTokenSource();
        var service = Service(state, new SerializedMutationLock(), cancelAfterRevoke: cancellation);

        var action = () => service.SetSystemAdminAsync("admin-a", false, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        state.EnabledSystemAdminCount.Should().Be(1);
        state.SnapshotCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Upstream_failure_after_dispatched_revoke_still_runs_post_verification()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        var service = Service(state, new SerializedMutationLock(), failAfterRevoke: true);

        var action = () => service.SetSystemAdminAsync("admin-a", false);

        await action.Should().ThrowAsync<IdentityUpstreamUnavailableException>();
        state.EnabledSystemAdminCount.Should().Be(1);
        state.SnapshotCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Uncertain_zero_admin_revoke_restores_only_the_previously_held_role()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        var service = Service(state, new SerializedMutationLock(), removeAllOnRevoke: true, failAfterRevoke: true);

        var action = () => service.SetSystemAdminAsync("admin-a", false);

        await action.Should().ThrowAsync<IdentityInvariantViolationException>();
        state.EnabledSystemAdminCount.Should().Be(1);
        state.Contains("admin-a").Should().BeTrue();
    }

    [Fact]
    public async Task Removing_absent_role_is_a_noop_and_never_grants_system_admin()
    {
        var state = new IdentityState(("admin-a", true), ("admin-b", true));
        var service = Service(state, new SerializedMutationLock());

        await service.SetSystemAdminAsync("ordinary-user", false);

        state.Contains("ordinary-user").Should().BeFalse();
        state.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task Disabling_an_already_disabled_identity_is_a_noop()
    {
        var state = new IdentityState(("admin-a", true), ("disabled-admin", false));
        var service = Service(state, new SerializedMutationLock());

        await service.SetEnabledAsync("disabled-admin", false);

        state.MutationCount.Should().Be(0);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static KeycloakAuthService Service(
        IdentityState state,
        ISystemAdminMutationLock mutationLock,
        bool removeAllOnRevoke = false,
        CancellationTokenSource? cancelAfterRevoke = null,
        bool failAfterRevoke = false)
    {
        var client = new HttpClient(new IdentityHandler(state, removeAllOnRevoke, cancelAfterRevoke, failAfterRevoke));
        var factory = new Mock<IKeycloakHttpClientFactory>();
        factory.Setup(x => x.CreateClient()).Returns(client);
        return new KeycloakAuthService(factory.Object, Configuration(), mutationLock,
            NullLogger<KeycloakAuthService>.Instance);
    }

    private static IConfiguration Configuration(string? breakGlassUserId = null) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = "https://identity.example.test/realms/test-realm",
            ["Keycloak:Realm"] = "test-realm",
            ["Keycloak:AdminClientId"] = "configured-admin",
            ["Keycloak:AdminClientSecret"] = "configured-secret",
            ["Keycloak:BreakGlassUserId"] = breakGlassUserId
        }).Build();

    private sealed class IdentityState(params (string Id, bool Enabled)[] admins)
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, bool> _admins = admins.ToDictionary(x => x.Id, x => x.Enabled);
        public int SnapshotCount { get; private set; }
        public int MutationCount { get; private set; }

        public int EnabledSystemAdminCount
        {
            get { lock (_sync) return _admins.Count(x => x.Value); }
        }

        public object[] Snapshot(int first, int max)
        {
            lock (_sync)
            {
                SnapshotCount++;
                return _admins.Skip(first).Take(max)
                    .Select(x => new { id = x.Key, enabled = x.Value }).Cast<object>().ToArray();
            }
        }

        public void Revoke(string userId)
        {
            lock (_sync)
            {
                MutationCount++;
                _admins.Remove(userId);
            }
        }

        public void RevokeAll()
        {
            lock (_sync)
            {
                MutationCount++;
                _admins.Clear();
            }
        }

        public void Grant(string userId)
        {
            lock (_sync)
            {
                MutationCount++;
                _admins[userId] = true;
            }
        }

        public bool Contains(string userId)
        {
            lock (_sync) return _admins.ContainsKey(userId);
        }

        public void SetEnabled(string userId, bool enabled)
        {
            lock (_sync)
            {
                if (_admins.ContainsKey(userId)) _admins[userId] = enabled;
                MutationCount++;
            }
        }

        public bool? GetEnabled(string userId)
        {
            lock (_sync) return _admins.TryGetValue(userId, out var enabled) ? enabled : null;
        }
    }

    private sealed class IdentityHandler(
        IdentityState state,
        bool removeAllOnRevoke,
        CancellationTokenSource? cancelAfterRevoke,
        bool failAfterRevoke) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new { access_token = "token", expires_in = 60 });
            if (path.EndsWith("/roles/SystemAdmin/users", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, state.Snapshot(QueryInt(request, "first"), QueryInt(request, "max")));
            if (path.EndsWith("/roles/SystemAdmin", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new { id = "system-admin-role", name = "SystemAdmin" });

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var usersIndex = Array.IndexOf(segments, "users");
            var userId = usersIndex >= 0 && usersIndex + 1 < segments.Length ? Uri.UnescapeDataString(segments[usersIndex + 1]) : string.Empty;
            if (request.Method == HttpMethod.Delete && path.EndsWith("/role-mappings/realm", StringComparison.Ordinal))
            {
                await Task.Delay(25, cancellationToken);
                if (removeAllOnRevoke) state.RevokeAll();
                else state.Revoke(userId);
                if (cancelAfterRevoke is not null)
                {
                    cancelAfterRevoke.Cancel();
                    throw new OperationCanceledException(cancellationToken);
                }
                if (failAfterRevoke) throw new HttpRequestException("synthetic uncertain network failure");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/role-mappings/realm", StringComparison.Ordinal))
            {
                state.Grant(userId);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Put && usersIndex >= 0)
            {
                var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                state.SetEnabled(userId, payload.RootElement.GetProperty("enabled").GetBoolean());
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && usersIndex >= 0)
            {
                var enabled = state.GetEnabled(userId);
                return enabled.HasValue
                    ? Json(HttpStatusCode.OK, new { id = userId, enabled = enabled.Value })
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };

        private static int QueryInt(HttpRequestMessage request, string name)
        {
            foreach (var part in request.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == name && int.TryParse(pair[1], out var value)) return value;
            }
            return 0;
        }
    }

    private sealed class SerializedMutationLock : ISystemAdminMutationLock
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _holders;
        public int MaximumConcurrentHolders { get; private set; }

        public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var holders = Interlocked.Increment(ref _holders);
                MaximumConcurrentHolders = Math.Max(MaximumConcurrentHolders, holders);
                await action(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _holders);
                _gate.Release();
            }
        }
    }
}
