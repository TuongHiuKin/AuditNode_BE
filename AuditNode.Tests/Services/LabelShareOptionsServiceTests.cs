using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class LabelShareOptionsServiceTests
{
    [Fact]
    public async Task Owner_receives_only_enabled_ranked_users_and_never_self()
    {
        await using var context = Context();
        var label = Label("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(service => service.ListUsersAsync("alice", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new("contains", "team-alice", null, true),
                new("prefix", "alice.ops", null, true),
                new("exact", "Alice", null, true),
                new("owner", "alice.owner", null, true),
                new("disabled", "alice.disabled", null, false)
            ]);

        var result = await Service(context, identities.Object, "owner")
            .GetAsync(label.Id, "alice", 0, 20);

        result.Should().NotBeNull();
        result!.Users.Select(user => user.Id).Should().Equal("exact", "prefix", "contains");
        result.SharesAllOwnerResources.Should().BeFalse();
    }

    [Fact]
    public async Task Non_owner_cannot_enumerate_the_identity_directory()
    {
        await using var context = Context();
        var label = Label("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>(MockBehavior.Strict);

        var result = await Service(context, identities.Object, "editor")
            .GetAsync(label.Id, "alice", 0, 20);

        result.Should().BeNull();
        identities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Owner_label_returns_explicit_all_owner_resources_warning_metadata()
    {
        await using var context = Context();
        var label = Label("owner", LabelKinds.Owner);
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(service => service.ListUsersAsync("alice", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Service(context, identities.Object, "owner")
            .GetAsync(label.Id, "alice", 0, 20);

        result!.SharesAllOwnerResources.Should().BeTrue();
        result.WarningCode.Should().Be("owner_label_shares_all_owner_resources");
    }

    [Fact]
    public async Task Service_defensively_caps_directory_results_to_twenty()
    {
        await using var context = Context();
        var label = Label("owner");
        context.Labels.Add(label);
        await context.SaveChangesAsync();
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(service => service.ListUsersAsync("user", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 50)
                .Select(index => new IdentityAdminUserDto(
                    $"id-{index}", $"user-{index:D2}", null, true))
                .ToList());

        var result = await Service(context, identities.Object, "owner")
            .GetAsync(label.Id, "user", 0, 99);

        result!.Users.Should().HaveCount(20);
    }

    private static LabelShareOptionsService Service(
        AuditDbContext context,
        IIdentityAdminService identities,
        string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(userId);
        return new LabelShareOptionsService(context, identities, currentUser.Object);
    }

    private static AuditDbContext Context()
    {
        return new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static Label Label(string owner, string kind = LabelKinds.Business) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner,
        Key = "domain", Value = Guid.NewGuid().ToString("N"), Kind = kind,
        IsProtected = kind == LabelKinds.Owner
    };
}
