using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class OwnerLabelServiceTests
{
    [Fact]
    public async Task Ensure_creates_one_protected_owner_label_with_immutable_owner_identity()
    {
        await using var context = CreateContext();
        var service = new OwnerLabelService(context, TimeProvider.System);

        await service.EnsureAsync("owner-user-id");
        await service.EnsureAsync("owner-user-id");

        var label = await context.Labels.SingleAsync();
        label.OwnerUserId.Should().Be("owner-user-id");
        label.Key.Should().Be("Owner");
        label.Value.Should().Be("owner-user-id");
        label.Kind.Should().Be(LabelKinds.Owner);
        label.IsProtected.Should().BeTrue();
    }

    [Fact]
    public async Task Ensure_preserves_an_existing_owner_label_without_creating_a_duplicate()
    {
        await using var context = CreateContext();
        var existing = new Label
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "owner-user-id",
            Key = "Owner",
            Value = "display-name",
            Kind = LabelKinds.Owner,
            IsProtected = true
        };
        context.Labels.Add(existing);
        await context.SaveChangesAsync();

        await new OwnerLabelService(context, TimeProvider.System).EnsureAsync("owner-user-id");

        (await context.Labels.CountAsync()).Should().Be(1);
        (await context.Labels.SingleAsync()).Id.Should().Be(existing.Id);
    }

    private static AuditDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuditDbContext(options);
    }
}
