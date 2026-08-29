using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class LabelGrantServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Owner_can_grant_editor_to_an_enabled_registered_user()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var identities = EnabledIdentity("editor");

        var result = await Service(context, "owner", identities.Object).CreateAsync(
            label.Id,
            new CreateLabelGrantDto("editor", LabelGrantPermissions.Editor, null));

        result.Status.Should().Be(LabelGrantMutationStatus.Success);
        result.Grant!.Permission.Should().Be(LabelGrantPermissions.Editor);
        var stored = await context.LabelGrants.IgnoreQueryFilters().SingleAsync();
        stored.GranteeUserId.Should().Be("editor");
        stored.TokenHash.Should().BeNull();
        stored.OwnerUserId.Should().Be("owner");
    }

    [Fact]
    public async Task Non_owner_and_disabled_target_are_rejected_without_creating_a_grant()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var disabled = new Mock<IIdentityAdminService>();
        disabled.Setup(service => service.GetUserAsync("disabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAdminUserDto("disabled", "disabled", null, false));

        var targetIdentity = EnabledIdentity("target");
        var nonOwner = await Service(context, "editor", targetIdentity.Object).CreateAsync(
            label.Id, new CreateLabelGrantDto("target", LabelGrantPermissions.Viewer, null));
        var disabledTarget = await Service(context, "owner", disabled.Object).CreateAsync(
            label.Id, new CreateLabelGrantDto("disabled", LabelGrantPermissions.Viewer, null));

        nonOwner.Status.Should().Be(LabelGrantMutationStatus.Denied);
        targetIdentity.Verify(
            service => service.GetUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a non-owner must not be able to probe the identity directory through grant creation");
        disabledTarget.Status.Should().Be(LabelGrantMutationStatus.Invalid);
        (await context.LabelGrants.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_and_revoke_require_the_current_version_and_fail_closed()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        var grant = UserGrant(label, "user", LabelGrantPermissions.Viewer);
        context.AddRange(label, grant);
        await context.SaveChangesAsync();
        var service = Service(context, "owner", EnabledIdentity("user").Object);

        var updated = await service.UpdateAsync(
            label.Id, grant.Id, new UpdateLabelGrantDto(LabelGrantPermissions.Editor, null, 1));
        var stale = await service.RevokeAsync(label.Id, grant.Id, 1);
        var revoked = await service.RevokeAsync(label.Id, grant.Id, 2);

        updated.Status.Should().Be(LabelGrantMutationStatus.Success);
        updated.Grant!.Version.Should().Be(2);
        stale.Status.Should().Be(LabelGrantMutationStatus.Conflict);
        revoked.Status.Should().Be(LabelGrantMutationStatus.Success);
        revoked.Grant!.Version.Should().Be(3);
        revoked.Grant.RevokedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Owner_consistency_is_rechecked_during_mutation()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        var grant = UserGrant(label, "user", LabelGrantPermissions.Viewer);
        grant.OwnerUserId = "different-owner";
        context.AddRange(label, grant);
        await context.SaveChangesAsync();

        var result = await Service(context, "owner", EnabledIdentity("user").Object)
            .UpdateAsync(label.Id, grant.Id, new UpdateLabelGrantDto(LabelGrantPermissions.Editor, null, 1));

        result.Status.Should().Be(LabelGrantMutationStatus.Denied);
    }

    [Fact]
    public async Task Create_regrants_after_atomically_revoking_an_expired_unrevoked_grant()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        var expired = UserGrant(label, "user", LabelGrantPermissions.Viewer);
        expired.ExpiresAt = Now.UtcDateTime;
        context.AddRange(label, expired);
        await context.SaveChangesAsync();

        var result = await Service(context, "owner", EnabledIdentity("user").Object).CreateAsync(
            label.Id,
            new CreateLabelGrantDto("user", LabelGrantPermissions.Editor, Now.AddHours(1)));

        result.Status.Should().Be(LabelGrantMutationStatus.Success);
        result.Grant!.Id.Should().NotBe(expired.Id);
        var stored = await context.LabelGrants.IgnoreQueryFilters()
            .OrderBy(grant => grant.CreatedAt)
            .ToListAsync();
        stored.Should().HaveCount(2);
        stored.Single(grant => grant.Id == expired.Id).Should().Match<LabelGrant>(grant =>
            grant.RevokedAt == Now.UtcDateTime && grant.Version == 2);
        stored.Single(grant => grant.Id == result.Grant.Id).Should().Match<LabelGrant>(grant =>
            grant.RevokedAt == null &&
            grant.Version == 1 &&
            grant.Permission == LabelGrantPermissions.Editor);
    }

    [Fact]
    public async Task Create_conflicts_with_an_active_unrevoked_grant_without_changing_it()
    {
        await using var context = Context();
        var label = BusinessLabel("owner");
        var active = UserGrant(label, "user", LabelGrantPermissions.Viewer);
        active.ExpiresAt = Now.AddMinutes(1).UtcDateTime;
        context.AddRange(label, active);
        await context.SaveChangesAsync();

        var result = await Service(context, "owner", EnabledIdentity("user").Object).CreateAsync(
            label.Id,
            new CreateLabelGrantDto("user", LabelGrantPermissions.Editor, null));

        result.Status.Should().Be(LabelGrantMutationStatus.Conflict);
        var stored = await context.LabelGrants.IgnoreQueryFilters().SingleAsync();
        stored.Id.Should().Be(active.Id);
        stored.RevokedAt.Should().BeNull();
        stored.Version.Should().Be(1);
    }

    private static LabelGrantService Service(
        AuditDbContext context,
        string currentUserId,
        IIdentityAdminService identities)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(currentUserId);
        return new LabelGrantService(
            context,
            currentUser.Object,
            identities,
            new FixedTimeProvider(Now),
            NullLogger<LabelGrantService>.Instance);
    }

    private static Mock<IIdentityAdminService> EnabledIdentity(string userId)
    {
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(service => service.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAdminUserDto(userId, userId, null, true));
        return identities;
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
        Key = "domain", Value = Guid.NewGuid().ToString("N"), Kind = LabelKinds.Business
    };

    private static LabelGrant UserGrant(Label label, string userId, string permission) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = label.OwnerUserId!, LabelId = label.Id,
        GranteeUserId = userId, Permission = permission, Version = 1,
        CreatedByUserId = label.OwnerUserId!
    };
}
