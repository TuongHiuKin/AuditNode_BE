using System.Security.Cryptography;
using System.Text;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class ShareTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_returns_raw_token_once_and_persists_only_a_viewer_hash_with_required_expiry()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var service = Service(context, "owner");

        var result = await service.CreateAsync(label.Id, Now.AddHours(1));

        result.Status.Should().Be(ShareTokenMutationStatus.Success);
        result.RawToken.Should().NotBeNullOrWhiteSpace();
        var stored = await context.LabelGrants.IgnoreQueryFilters().SingleAsync();
        stored.Permission.Should().Be(LabelGrantPermissions.Viewer);
        stored.GranteeUserId.Should().BeNull();
        stored.ExpiresAt.Should().Be(Now.AddHours(1).UtcDateTime);
        stored.TokenHash.Should().Be(Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.RawToken!))).ToLowerInvariant());
        stored.TokenHash.Should().NotContain(result.RawToken!);
    }

    [Fact]
    public async Task Resolve_accepts_only_active_tokens_and_returns_the_same_generic_denial_otherwise()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var ownerService = Service(context, "owner");
        var created = await ownerService.CreateAsync(label.Id, Now.AddMinutes(30));

        var valid = await Service(context, null).ResolveAsync(created.RawToken!);
        var invalid = await Service(context, null).ResolveAsync("invalid-token");
        var empty = await Service(context, null).ResolveAsync(string.Empty);

        valid.Should().Be(new ShareTokenResolutionDto(label.Id, "owner", LabelGrantPermissions.Viewer));
        invalid.Should().BeNull();
        empty.Should().BeNull();
    }

    [Fact]
    public async Task Token_requires_future_expiry_and_non_owner_cannot_create_or_revoke_it()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();

        var invalidExpiry = await Service(context, "owner").CreateAsync(label.Id, Now);
        var nonOwner = await Service(context, "other").CreateAsync(label.Id, Now.AddHours(1));
        var created = await Service(context, "owner").CreateAsync(label.Id, Now.AddHours(1));
        var stored = await context.LabelGrants.IgnoreQueryFilters().SingleAsync();
        var deniedRevoke = await Service(context, "other").RevokeAsync(label.Id, stored.Id, stored.Version);

        invalidExpiry.Status.Should().Be(ShareTokenMutationStatus.Invalid);
        nonOwner.Status.Should().Be(ShareTokenMutationStatus.Denied);
        created.Status.Should().Be(ShareTokenMutationStatus.Success);
        deniedRevoke.Status.Should().Be(ShareTokenMutationStatus.Denied);
    }

    [Fact]
    public async Task Expired_and_revoked_tokens_have_the_same_generic_resolution_denial()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var service = Service(context, "owner");
        var expired = await service.CreateAsync(label.Id, Now.AddMinutes(1));
        var revoked = await service.CreateAsync(label.Id, Now.AddMinutes(2));
        var expiredGrant = await context.LabelGrants.IgnoreQueryFilters()
            .SingleAsync(grant => grant.Id == expired.GrantId);
        expiredGrant.ExpiresAt = Now.UtcDateTime;
        await context.SaveChangesAsync();
        await service.RevokeAsync(label.Id, revoked.GrantId!.Value, revoked.Version!.Value);

        (await Service(context, null).ResolveAsync(expired.RawToken!)).Should().BeNull();
        (await Service(context, null).ResolveAsync(revoked.RawToken!)).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_rejects_huge_and_malformed_values_but_accepts_the_generated_base64url_format()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var service = Service(context, "owner");
        var created = await service.CreateAsync(label.Id, Now.AddMinutes(5));

        created.RawToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        (await Service(context, null).ResolveAsync(new string('A', 1_000_000))).Should().BeNull();
        (await Service(context, null).ResolveAsync(new string('A', 42))).Should().BeNull();
        (await Service(context, null).ResolveAsync(new string('A', 42) + "+")).Should().BeNull();
        (await Service(context, null).ResolveAsync(new string('A', 42) + "=")).Should().BeNull();
        (await Service(context, null).ResolveAsync(created.RawToken!)).Should().NotBeNull();
    }

    [Fact]
    public async Task Owner_label_link_creation_exposes_all_owner_resources_warning_metadata()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        label.Kind = LabelKinds.Owner;
        label.IsProtected = true;
        context.Labels.Add(label);
        await context.SaveChangesAsync();

        var result = await Service(context, "owner").CreateAsync(label.Id, Now.AddHours(1));

        result.SharesAllOwnerResources.Should().BeTrue();
        result.WarningCode.Should().Be(LabelShareWarningCodes.OwnerLabelSharesAllOwnerResources);
        var resolution = await Service(context, null).ResolveAsync(result.RawToken!);
        resolution!.SharesAllOwnerResources.Should().BeTrue();
        resolution.WarningCode.Should().Be(LabelShareWarningCodes.OwnerLabelSharesAllOwnerResources);
    }

    [Fact]
    public async Task Creation_security_log_never_contains_the_raw_token()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var logger = new CapturingLogger<ShareTokenService>();

        var result = await Service(context, "owner", logger)
            .CreateAsync(label.Id, Now.AddHours(1));

        logger.Messages.Should().NotContain(message =>
            message.Contains(result.RawToken!, StringComparison.Ordinal));
    }

    private static ShareTokenService Service(
        AuditDbContext context,
        string? currentUserId,
        ILogger<ShareTokenService>? logger = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(currentUserId);
        return new ShareTokenService(
            context,
            currentUser.Object,
            new FixedTimeProvider(Now),
            logger ?? NullLogger<ShareTokenService>.Instance);
    }

    private static AuditDbContext Context()
    {
        var tenant = new Mock<ITenantProvider>();
        return new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant.Object);
    }

    private static Label BusinessLabel(string owner) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = Guid.NewGuid(), OwnerUserId = owner,
        Key = "share", Value = Guid.NewGuid().ToString("N"), Kind = LabelKinds.Business
    };

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
