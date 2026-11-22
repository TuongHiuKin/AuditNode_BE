using AuditNode.API.Controllers;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class DatacentersControllerTests
{
    [Fact]
    public async Task GetDatacenters_WithInvalidView_ReturnsBadRequestWithoutQueryingService()
    {
        var service = new Mock<IDatacenterService>(MockBehavior.Strict);
        var controller = new DatacentersController(service.Object);

        var response = await controller.GetDatacenters(view: "not-a-catalog-view");

        response.Result.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }
}
