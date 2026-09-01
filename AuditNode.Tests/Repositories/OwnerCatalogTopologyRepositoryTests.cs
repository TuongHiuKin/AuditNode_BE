using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace AuditNode.Tests.Repositories;

public sealed class OwnerCatalogTopologyRepositoryTests
{
    [Fact]
    public async Task Full_save_rejects_foreign_owner_resource_without_persisting_graph()
    {
        await using var context = CreateContext();
        var foreignDatacenter = new Datacenter
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner-b", Name = "Foreign", Location = "Restricted"
        };
        var foreignServer = new Server
        {
            Id = Guid.NewGuid(), OwnerUserId = "owner-b", DatacenterId = foreignDatacenter.Id,
            Hostname = "foreign-server", IpAddress = "10.0.0.2", OsType = "Linux",
            Environment = "Production", Status = "Active"
        };
        context.AddRange(foreignDatacenter, foreignServer);
        await context.SaveChangesAsync();

        var state = new SaveTopologyStateDto
        {
            Version = 0,
            Nodes =
            [
                new TopologyNodeDto
                {
                    Id = Guid.NewGuid(), NodeType = "server", Label = "foreign",
                    ReferenceId = foreignServer.Id
                }
            ],
            Edges = [],
            Dependencies = []
        };

        var result = await Repository(context, "owner-a").SaveTopologyStateAsync(state);

        result.Should().Be(TopologyStateStatus.InvalidReference);
        (await context.TopologyNodes.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await context.TopologyEdges.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await context.OwnerCatalogStates.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Scoped_read_masks_hidden_edge_endpoint_without_leaking_original_identifiers()
    {
        await using var context = CreateContext();
        var visibleServerId = Guid.NewGuid();
        var hiddenServerId = Guid.NewGuid();
        var visibleNodeId = Guid.NewGuid();
        var hiddenNodeId = Guid.NewGuid();
        var edgeId = Guid.NewGuid();
        context.AddRange(
            new OwnerCatalogState { OwnerUserId = "owner", TopologyVersion = 7 },
            new TopologyNode
            {
                Id = visibleNodeId, OwnerUserId = "owner", NodeType = "server",
                Label = "Visible server", ReferenceId = visibleServerId
            },
            new TopologyNode
            {
                Id = hiddenNodeId, OwnerUserId = "owner", NodeType = "server",
                Label = "Secret server", ReferenceId = hiddenServerId
            },
            new TopologyEdge
            {
                Id = edgeId, OwnerUserId = "owner", SourceNodeId = visibleNodeId,
                TargetNodeId = hiddenNodeId, SourceHandle = "source", TargetHandle = "target",
                EdgeType = "network", Label = "secret connection", ReferenceId = Guid.NewGuid()
            });
        await context.SaveChangesAsync();

        var access = new OwnerGraphAccessDto(
            "owner",
            LabelEffectivePermission.Viewer,
            new HashSet<Guid> { visibleServerId },
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>());
        var repository = Repository(context, "viewer", access);

        var result = await repository.GetTopologyStateAsync("owner");

        result.Version.Should().Be(7);
        result.Nodes.Should().ContainSingle(node => node.Id == visibleNodeId && !node.IsRestricted);
        var restricted = result.Nodes.Should().ContainSingle(node => node.IsRestricted).Subject;
        restricted.NodeType.Should().Be("restricted");
        restricted.Label.Should().Be("External Resource (Restricted)");
        restricted.Id.Should().NotBe(hiddenNodeId);
        restricted.ReferenceId.Should().BeNull();

        var edge = result.Edges.Should().ContainSingle().Subject;
        edge.Id.Should().NotBe(edgeId);
        edge.SourceNodeId.Should().Be(visibleNodeId);
        edge.TargetNodeId.Should().Be(restricted.Id);
        edge.EdgeType.Should().Be("restricted");
        edge.Label.Should().BeEmpty();
        edge.ReferenceId.Should().BeNull();
        result.Nodes.Should().NotContain(node => node.Id == hiddenNodeId || node.ReferenceId == hiddenServerId);
    }

    private static TopologyRepository Repository(
        AuditDbContext context,
        string userId,
        OwnerGraphAccessDto? access = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(item => item.UserId).Returns(userId);
        var graphAccess = new Mock<IOwnerGraphAccessService>();
        graphAccess.Setup(service => service.ResolveAsync(
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(access ?? new OwnerGraphAccessDto(
                userId,
                LabelEffectivePermission.Owner,
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        return new TopologyRepository(context, currentUser.Object, graphAccess.Object);
    }

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }
}
