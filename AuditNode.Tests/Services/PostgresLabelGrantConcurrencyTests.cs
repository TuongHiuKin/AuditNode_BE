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

public sealed class PostgresLabelGrantConcurrencyTests
{
    [PostgresIntegrationFact]
    public async Task Concurrent_updates_with_the_same_version_allow_exactly_one_winner()
    {
        var fixture = await SeedAsync();
        await using var firstContext = Context(fixture.WorkspaceId);
        await using var secondContext = Context(fixture.WorkspaceId);

        var results = await Task.WhenAll(
            Service(firstContext, fixture.OwnerUserId, fixture.GranteeUserId).UpdateAsync(
                fixture.LabelId,
                fixture.GrantId,
                new UpdateLabelGrantDto(LabelGrantPermissions.Editor, null, 1)),
            Service(secondContext, fixture.OwnerUserId, fixture.GranteeUserId).UpdateAsync(
                fixture.LabelId,
                fixture.GrantId,
                new UpdateLabelGrantDto(LabelGrantPermissions.Viewer, DateTimeOffset.UtcNow.AddHours(1), 1)));

        results.Select(result => result.Status).Should().BeEquivalentTo(
            [LabelGrantMutationStatus.Success, LabelGrantMutationStatus.Conflict]);
        await using var verification = Context(fixture.WorkspaceId);
        (await verification.LabelGrants.IgnoreQueryFilters().SingleAsync(grant => grant.Id == fixture.GrantId))
            .Version.Should().Be(2);
    }

    private static async Task<Fixture> SeedAsync()
    {
        var fixture = new Fixture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"owner-{Guid.NewGuid():N}",
            $"grantee-{Guid.NewGuid():N}");
        await using var context = Context(fixture.WorkspaceId);
        context.Workspaces.Add(new Workspace
        {
            Id = fixture.WorkspaceId,
            Name = $"Label grant concurrency {fixture.WorkspaceId:N}",
            OwnerUserId = fixture.OwnerUserId
        });
        context.Labels.Add(new Label
        {
            Id = fixture.LabelId,
            WorkspaceId = fixture.WorkspaceId,
            OwnerUserId = fixture.OwnerUserId,
            Key = "concurrency",
            Value = fixture.LabelId.ToString("N"),
            Kind = LabelKinds.Business
        });
        context.LabelGrants.Add(new LabelGrant
        {
            Id = fixture.GrantId,
            LabelId = fixture.LabelId,
            OwnerUserId = fixture.OwnerUserId,
            GranteeUserId = fixture.GranteeUserId,
            Permission = LabelGrantPermissions.Viewer,
            Version = 1,
            CreatedByUserId = fixture.OwnerUserId
        });
        await context.SaveChangesAsync();
        return fixture;
    }

    private static LabelGrantService Service(
        AuditDbContext context,
        string ownerUserId,
        string granteeUserId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(ownerUserId);
        var identities = new Mock<IIdentityAdminService>();
        identities.Setup(service => service.GetUserAsync(granteeUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAdminUserDto(granteeUserId, granteeUserId, null, true));
        return new LabelGrantService(
            context,
            currentUser.Object,
            identities.Object,
            TimeProvider.System,
            NullLogger<LabelGrantService>.Instance);
    }

    private static AuditDbContext Context(Guid workspaceId)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(provider => provider.WorkspaceId).Returns(workspaceId);
        return new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseNpgsql(Environment.GetEnvironmentVariable("AUDITNODE_TEST_POSTGRES")!)
                .Options,
            tenant.Object);
    }

    private sealed record Fixture(
        Guid WorkspaceId,
        Guid LabelId,
        Guid GrantId,
        string OwnerUserId,
        string GranteeUserId);
}
