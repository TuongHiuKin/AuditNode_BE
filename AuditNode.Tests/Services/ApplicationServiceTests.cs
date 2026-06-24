using AuditNode.Application.DTOs;
using AuditNode.Infrastructure.Services;
using AuditNode.Infrastructure.Data;
using AuditNode.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AuditNode.Tests.Services;

public class ApplicationServiceTests
{
    private AuditDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;        
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnApplications()
    {
        using var context = GetDbContext();
        context.Applications.Add(new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App1", OwnerTeam = "T1" });
        await context.SaveChangesAsync();

        var service = new ApplicationService(context);
        var result = await service.GetAllAsync();

        result.Should().HaveCount(1);
        result.First().AppCode.Should().Be("A1");
    }

    [Fact]
    public async Task GetAllAsync_WithLabels_ShouldFilterCorrectly()
    {
        using var context = GetDbContext();
        
        var labelTier1 = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "1" };
        var labelTier2 = new Label { Id = Guid.NewGuid(), Key = "tier", Value = "2" };
        
        var app1 = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A1", AppName = "App 1", OwnerTeam = "T1", Labels = new List<Label> { labelTier1 } };
        var app2 = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A2", AppName = "App 2", OwnerTeam = "T1", Labels = new List<Label> { labelTier2 } };
        var app3 = new AuditNode.Domain.Entities.Application { Id = Guid.NewGuid(), AppCode = "A3", AppName = "App 3", OwnerTeam = "T1" };

        context.Applications.AddRange(app1, app2, app3);
        await context.SaveChangesAsync();

        var service = new ApplicationService(context);

        var resultTier1 = await service.GetAllAsync(new[] { "1" });
        resultTier1.Should().HaveCount(1);
        resultTier1.First().AppCode.Should().Be("A1");

        var resultBoth = await service.GetAllAsync(new[] { "tier" });
        resultBoth.Should().HaveCount(2);

        var resultNone = await service.GetAllAsync(new[] { "nonexistent" });
        resultNone.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnApp_WhenExists()
    {
        using var context = GetDbContext();
        var id = Guid.NewGuid();
        context.Applications.Add(new AuditNode.Domain.Entities.Application { Id = id, AppCode = "A1", AppName = "App1", OwnerTeam = "T1" });
        await context.SaveChangesAsync();

        var service = new ApplicationService(context);
        var result = await service.GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }

    [Fact]
    public async Task CreateAsync_ShouldAddNewApp_AndReturnDto()
    {
        using var context = GetDbContext();
        var service = new ApplicationService(context);
        var dto = new CreateApplicationDto { AppCode = "NEW1", AppName = "New App", OwnerTeam = "Team 1" };

        var result = await service.CreateAsync(dto);

        result.Should().NotBeNull();
        result.AppCode.Should().Be("NEW1");
        
        var inDb = await context.Applications.FirstOrDefaultAsync(a => a.AppCode == "NEW1");
        inDb.Should().NotBeNull();
    }
}
