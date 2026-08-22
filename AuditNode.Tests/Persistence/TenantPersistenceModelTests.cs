using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuditNode.Tests.Persistence;

public class TenantPersistenceModelTests
{
    private static AuditDbContext CreateContext(Guid workspaceId)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(workspaceId);
        return new AuditDbContext(options, tenant.Object);
    }

    [Fact]
    public void Model_ShouldConfigureWorkspaceScopedEntitiesAndViews()
    {
        using var context = CreateContext(Guid.NewGuid());

        var datacenter = context.Model.FindEntityType(typeof(Datacenter))!;
        var topologyView = context.Model.FindEntityType(typeof(TopologyView))!;
        var dependencyView = context.Model.FindEntityType(typeof(DependencyView))!;

        Assert.NotNull(datacenter.FindProperty(nameof(Datacenter.WorkspaceId)));
        Assert.NotEmpty(datacenter.GetDeclaredQueryFilters());
        Assert.NotNull(topologyView.FindProperty(nameof(TopologyView.WorkspaceId)));
        Assert.NotEmpty(topologyView.GetDeclaredQueryFilters());
        Assert.NotNull(dependencyView.FindProperty(nameof(DependencyView.WorkspaceId)));
        Assert.NotEmpty(dependencyView.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Model_ShouldUseTenantScopedUniqueIndexes()
    {
        using var context = CreateContext(Guid.NewGuid());
        var serverIndexes = context.Model.FindEntityType(typeof(Server))!.GetIndexes();
        var applicationIndexes = context.Model.FindEntityType(typeof(AuditNode.Domain.Entities.Application))!.GetIndexes();

        Assert.Contains(serverIndexes, index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(Server.WorkspaceId), nameof(Server.IpAddress)]));
        Assert.DoesNotContain(serverIndexes, index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(Server.IpAddress)]));
        Assert.Contains(applicationIndexes, index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(Server.WorkspaceId), nameof(AuditNode.Domain.Entities.Application.AppCode)]));
    }

    [Fact]
    public void Model_ShouldConfigureCompositeWorkspaceMemberKey()
    {
        using var context = CreateContext(Guid.NewGuid());
        var member = context.Model.FindEntityType(typeof(WorkspaceMember))!;

        Assert.Equal(
            [nameof(WorkspaceMember.WorkspaceId), nameof(WorkspaceMember.UserId)],
            member.FindPrimaryKey()!.Properties.Select(x => x.Name));
    }

    [Fact]
    public void Model_ShouldUseWorkspaceInTenantRelationshipForeignKeys()
    {
        using var context = CreateContext(Guid.NewGuid());
        var server = context.Model.FindEntityType(typeof(Server))!;
        var portMapping = context.Model.FindEntityType(typeof(PortMapping))!;
        var dependency = context.Model.FindEntityType(typeof(AppDependency))!;

        Assert.Contains(server.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Datacenter) &&
            foreignKey.Properties.Select(x => x.Name).SequenceEqual([nameof(Server.WorkspaceId), nameof(Server.DatacenterId)]));
        Assert.Contains(portMapping.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Server) &&
            foreignKey.Properties.Select(x => x.Name).SequenceEqual([nameof(PortMapping.WorkspaceId), nameof(PortMapping.ServerId)]));
        Assert.Equal(3, dependency.GetForeignKeys().Count(foreignKey =>
            foreignKey.Properties.First().Name == nameof(AppDependency.WorkspaceId) &&
            foreignKey.PrincipalEntityType.ClrType != typeof(Workspace)));
    }

    [Fact]
    public void ServerLabelJoin_ShouldMakeCrossWorkspaceAssociationImpossible()
    {
        using var context = CreateContext(Guid.NewGuid());
        var link = context.Model.GetEntityTypes()
            .SingleOrDefault(entity => entity.ClrType?.Name == "ServerLabel");

        link.Should().NotBeNull("server labels require an explicit tenant-scoped join entity");
        link!.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal(
            "WorkspaceId", "ServerId", "LabelId");
        link.GetDeclaredQueryFilters().Should().NotBeEmpty();

        link.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Server) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(new[] { "WorkspaceId", "ServerId" }) &&
            foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(new[] { "WorkspaceId", "Id" }));
        link.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Label) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(new[] { "WorkspaceId", "LabelId" }) &&
            foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(new[] { "WorkspaceId", "Id" }));
    }

    [Fact]
    public async Task DatacenterQueryFilter_ShouldReturnOnlyCurrentWorkspace()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var seedTenant = new Mock<ITenantProvider>();
        seedTenant.SetupGet(x => x.WorkspaceId).Returns((Guid?)null);
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        await using (var seed = new AuditDbContext(options, seedTenant.Object))
        {
            seed.Datacenters.AddRange(
                new Datacenter { Id = Guid.NewGuid(), WorkspaceId = workspaceA, Name = "A", Location = "A" },
                new Datacenter { Id = Guid.NewGuid(), WorkspaceId = workspaceB, Name = "B", Location = "B" });
            await seed.SaveChangesAsync();
        }

        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.WorkspaceId).Returns(workspaceA);
        await using var context = new AuditDbContext(options, tenant.Object);

        var result = await context.Datacenters.ToListAsync();

        result.Should().ContainSingle().Which.WorkspaceId.Should().Be(workspaceA);
    }
}
