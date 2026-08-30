using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public sealed class LabelsControllerTests
{
    [Fact]
    public async Task Get_labels_defaults_to_mine_cursor_page()
    {
        var service = new Mock<ILabelCatalogService>();
        var page = new CursorPageDto<CatalogLabelDto>(
            [new CatalogLabelDto { Id = Guid.NewGuid(), Key = "env", Value = "prod", OwnerUserId = "me" }],
            null,
            false);
        service.Setup(value => value.GetLabelsAsync(
                It.Is<CatalogPageQuery>(query => query.View == CatalogView.Mine && query.Limit == 25),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await new LabelsController(service.Object).GetLabels();

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task Get_labels_requests_shared_only_when_explicit()
    {
        var service = new Mock<ILabelCatalogService>();
        service.Setup(value => value.GetLabelsAsync(
                It.Is<CatalogPageQuery>(query => query.View == CatalogView.Shared && query.Limit == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorPageDto<CatalogLabelDto>([], null, false));

        await new LabelsController(service.Object).GetLabels("shared", 10);

        service.Verify(value => value.GetLabelsAsync(
            It.Is<CatalogPageQuery>(query => query.View == CatalogView.Shared),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
