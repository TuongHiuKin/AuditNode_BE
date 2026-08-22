using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public class ServersControllerTests
{
    private readonly Mock<IServerService> _service = new();

    [Fact]
    public async Task Get_by_id_returns_bad_request_for_empty_id()
    {
        var result = await Controller().GetServer(Guid.Empty);

        ResultOf(result).Should().BeOfType<BadRequestObjectResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_by_id_returns_not_found_when_server_is_not_visible_to_tenant()
    {
        var id = Guid.NewGuid();
        _service.Setup(x => x.GetServerAsync(id)).ReturnsAsync((ServerResponseDto?)null);

        var result = await Controller().GetServer(id);

        ResultOf(result).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Create_returns_created_at_get_route()
    {
        var dto = ValidCreate();
        var created = new ServerResponseDto { Id = Guid.NewGuid(), IpAddress = dto.IpAddress };
        _service.Setup(x => x.CreateServerAsync(dto))
            .ReturnsAsync(new ServerOperationResult(ServerOperationStatus.Success, created));

        var result = await Controller().CreateServer(dto);

        var createdResult = ResultOf(result).Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(ServersController.GetServer));
        createdResult.RouteValues!["id"].Should().Be(created.Id);
    }

    [Fact]
    public async Task Create_returns_conflict_for_duplicate_ip()
    {
        var dto = ValidCreate();
        _service.Setup(x => x.CreateServerAsync(dto))
            .ReturnsAsync(new ServerOperationResult(ServerOperationStatus.DuplicateIp));

        var result = await Controller().CreateServer(dto);

        ResultOf(result).Should().BeOfType<ConflictObjectResult>();
    }



    [Fact]
    public async Task Update_returns_bad_request_for_unknown_datacenter_in_workspace()
    {
        var id = Guid.NewGuid();
        var dto = ValidUpdate();
        _service.Setup(x => x.UpdateServerAsync(id, dto))
            .ReturnsAsync(new ServerOperationResult(ServerOperationStatus.DatacenterNotFound));

        var result = await Controller().UpdateServer(id, dto);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_returns_not_found_when_server_is_not_visible_to_tenant()
    {
        var id = Guid.NewGuid();
        _service.Setup(x => x.PurgeServerAsync(id)).ReturnsAsync(ServerOperationStatus.NotFound);

        var result = await Controller().DeleteServer(id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Unexpected_exception_returns_safe_500_without_exception_message()
    {
        const string secretMessage = "database host and password";
        _service.Setup(x => x.GetServerAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException(secretMessage));

        var result = await Controller().GetServer(Guid.NewGuid());

        var objectResult = ResultOf(result).Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
        objectResult.Value.Should().BeOfType<ProblemDetails>();
        objectResult.Value!.ToString().Should().NotContain(secretMessage);
    }

    private ServersController Controller() => new(_service.Object);

    private static IActionResult ResultOf<T>(ActionResult<T> result) => result.Result!;

    private static CreateServerDto ValidCreate() => new()
    {
        DatacenterId = Guid.NewGuid(),
        IpAddress = "10.20.30.40",
        Hostname = "srv-01",
        OsType = "Linux",
        Environment = "Production",
        Status = "Active"
    };

    private static UpdateServerDto ValidUpdate() => new()
    {
        DatacenterId = Guid.NewGuid(),
        IpAddress = "10.20.30.41",
        Hostname = "srv-01",
        OsType = "Linux",
        Environment = "Production",
        Status = "Active"
    };
}

