using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace AuditNode.Tests.Persistence;

public sealed class GlobalCatalogPersistenceModelTests
{
    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(provider => provider.WorkspaceId).Returns(Guid.NewGuid());
        return new AuditDbContext(options, tenant.Object);
    }

    [Fact]
    public void BusinessResources_ShouldExposeTransitionalOwnerIdentity()
    {
        using var context = CreateContext();
        Type[] resourceTypes =
        [
            typeof(Datacenter),
            typeof(Server),
            typeof(AuditNode.Domain.Entities.Application),
            typeof(PortMapping),
            typeof(Label),
            typeof(ServerLabel),
            typeof(ApplicationLabel),
            typeof(AppDependency),
            typeof(TopologyNode),
            typeof(TopologyEdge)
        ];

        foreach (var resourceType in resourceTypes)
        {
            var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(resourceType);
            entityType.Should().NotBeNull();

            var ownerProperty = entityType!.FindProperty("OwnerUserId");
            ownerProperty.Should().NotBeNull($"{resourceType.Name} must carry catalog ownership");
            ownerProperty!.GetMaxLength().Should().Be(100);
            ownerProperty.IsNullable.Should().BeTrue(
                "Phase 1 is additive and cannot assign trustworthy ownership to legacy rows");
        }
    }

    [Fact]
    public void Label_ShouldEnforceKindsAndProtectedOwnerLabel()
    {
        using var context = CreateContext();
        var label = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Label))!;

        label.FindProperty(nameof(Label.Kind))!.GetDefaultValue().Should().Be(LabelKinds.Business);
        label.FindProperty(nameof(Label.IsProtected)).Should().NotBeNull();

        var checks = label.GetCheckConstraints().ToDictionary(
            constraint => constraint.Name!,
            constraint => constraint.Sql);
        checks["ck_labels_kind"].Should().Contain("owner").And.Contain("business");
        checks["ck_labels_owner_protected"].Should().Contain("kind <> 'owner' OR is_protected");

        label.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Label.WorkspaceId), nameof(Label.OwnerUserId), nameof(Label.Key), nameof(Label.Value) }));
        label.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.GetFilter() == "kind = 'owner' AND owner_user_id IS NOT NULL");
    }

    [Fact]
    public void LabelGrant_ShouldSeparateUserPermissionsFromAnonymousViewerLinks()
    {
        using var context = CreateContext();
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var grant = designModel.FindEntityType(typeof(LabelGrant))!;

        grant.FindPrimaryKey()!.Properties.Should().ContainSingle()
            .Which.Name.Should().Be(nameof(LabelGrant.Id));
        grant.FindProperty(nameof(LabelGrant.Version))!.IsConcurrencyToken.Should().BeTrue();

        var checks = grant.GetCheckConstraints().ToDictionary(
            constraint => constraint.Name!,
            constraint => constraint.Sql);
        checks["ck_label_grants_subject"].Should().Contain("grantee_user_id").And.Contain("token_hash");
        checks["ck_label_grants_permission"].Should().Contain("viewer").And.Contain("editor");
        checks["ck_label_grants_anonymous_viewer"].Should().Contain("token_hash IS NULL OR permission = 'viewer'");
        checks["ck_label_grants_token_expiry"].Should().Contain("token_hash IS NULL OR expires_at IS NOT NULL");

        grant.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(LabelGrant.TokenHash) }));
        grant.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(LabelGrant.LabelId), nameof(LabelGrant.GranteeUserId) }) &&
            index.GetFilter() == "revoked_at IS NULL AND grantee_user_id IS NOT NULL");

        designModel.GetEntityTypes().Should().NotContain(entity =>
            entity.ClrType.Name.Contains("Invite", StringComparison.OrdinalIgnoreCase),
            "Editor access is selected by the owner and stored directly as a user-bound grant");
    }

    [Fact]
    public void OwnerCatalogState_ShouldBeOwnerRevisionBoundary()
    {
        using var context = CreateContext();
        var state = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OwnerCatalogState))!;

        state.FindPrimaryKey()!.Properties.Should().ContainSingle()
            .Which.Name.Should().Be(nameof(OwnerCatalogState.OwnerUserId));
        state.FindProperty(nameof(OwnerCatalogState.OwnerUserId))!.GetMaxLength().Should().Be(100);
        state.FindProperty(nameof(OwnerCatalogState.TopologyVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void ReadOnlyViews_ShouldExposeTransitionalOwnerProjection()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var viewType in new[] { typeof(TopologyView), typeof(DependencyView) })
        {
            var owner = model.FindEntityType(viewType)!.FindProperty("OwnerUserId");
            owner.Should().NotBeNull();
            owner!.IsNullable.Should().BeTrue();
            owner.GetMaxLength().Should().Be(100);
            owner.GetColumnName().Should().Be("owner_user_id");
        }
    }
}
