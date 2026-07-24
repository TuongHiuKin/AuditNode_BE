using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AuditNode.Application.Interfaces;
using Moq;

namespace AuditNode.Tests.Controllers;

public class LabelsControllerTests
{
    private DbContextOptions<AuditDbContext> CreateNewContextOptions()
    {
        return new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task GetLabels_ReturnsDistinctLabels()
    {
        // Arrange
        var options = CreateNewContextOptions();
        var mockTenantProvider = new Mock<ITenantProvider>();
        var workspaceId = Guid.NewGuid();
        mockTenantProvider.Setup(t => t.WorkspaceId).Returns(workspaceId);

        using (var context = new AuditDbContext(options, mockTenantProvider.Object))
        {
            context.Labels.Add(new Label { Id = Guid.NewGuid(), Key = "env", Value = "prod", WorkspaceId = workspaceId });
            context.Labels.Add(new Label { Id = Guid.NewGuid(), Key = "env", Value = "prod", WorkspaceId = workspaceId }); // Duplicate
            context.Labels.Add(new Label { Id = Guid.NewGuid(), Key = "tier", Value = "frontend", WorkspaceId = workspaceId });
            await context.SaveChangesAsync();
        }

        using (var context = new AuditDbContext(options, mockTenantProvider.Object))
        {
            var controller = new LabelsController(context);

            // Act
            var result = await controller.GetLabels();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var labels = okResult.Value.Should().BeAssignableTo<IEnumerable<LabelDto>>().Subject;
            
            labels.Should().HaveCount(2); // Should remove duplicates
            labels.Should().Contain(l => l.Key == "env" && l.Value == "prod");
            labels.Should().Contain(l => l.Key == "tier" && l.Value == "frontend");
        }
    }
}

