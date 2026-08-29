using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public sealed class LabelGrantsControllerTests
{
    [Fact]
    public void Controller_is_authenticated_and_exposes_the_required_grant_route()
    {
        typeof(LabelGrantsController).Should().BeDecoratedWith<AuthorizeAttribute>();
        typeof(LabelGrantsController).GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>().Single().Template.Should().Be("api/v1/labels/{labelId:guid}/grants");
    }

    [Fact]
    public void Actions_declare_exact_success_and_failure_response_contracts()
    {
        ResponseContracts(nameof(LabelGrantsController.List)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status200OK, typeof(IReadOnlyList<LabelGrantDto>)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void))
        ]);
        ResponseContracts(nameof(LabelGrantsController.Create)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status201Created, typeof(LabelGrantDto)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status409Conflict, typeof(void))
        ]);
        ResponseContracts(nameof(LabelGrantsController.Create))
            .Should().NotContain(contract => contract.StatusCode == StatusCodes.Status200OK,
                "ApiExplorer must not infer 200 for create");
        ResponseContracts(nameof(LabelGrantsController.Update)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status200OK, typeof(LabelGrantDto)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status409Conflict, typeof(void))
        ]);
        ResponseContracts(nameof(LabelGrantsController.Revoke)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status204NoContent, typeof(void)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status409Conflict, typeof(void))
        ]);
        ResponseContracts(nameof(LabelGrantsController.Options)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status200OK, typeof(LabelShareOptionsDto)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status429TooManyRequests, typeof(void))
        ]);
    }

    [Fact]
    public async Task List_maps_missing_and_forbidden_to_the_same_non_disclosing_response()
    {
        var grants = new Mock<ILabelGrantService>();
        grants.Setup(service => service.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<LabelGrantDto>?)null);

        var result = await Controller(grants.Object).List(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData(LabelGrantMutationStatus.Denied, StatusCodes.Status404NotFound)]
    [InlineData(LabelGrantMutationStatus.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(LabelGrantMutationStatus.Conflict, StatusCodes.Status409Conflict)]
    public async Task Create_maps_service_failures_safely(
        LabelGrantMutationStatus status,
        int expectedStatus)
    {
        var grants = new Mock<ILabelGrantService>();
        grants.Setup(service => service.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<CreateLabelGrantDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabelGrantMutationResult(status));

        var result = await Controller(grants.Object).Create(
            Guid.NewGuid(), new CreateLabelGrantDto("user", "viewer", null));

        result.Result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Create_returns_the_user_bound_grant_once_created()
    {
        var labelId = Guid.NewGuid();
        var dto = new LabelGrantDto(Guid.NewGuid(), labelId, "user", "editor", null, null, 1);
        var grants = new Mock<ILabelGrantService>();
        grants.Setup(service => service.CreateAsync(
                labelId, It.IsAny<CreateLabelGrantDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabelGrantMutationResult(LabelGrantMutationStatus.Success, dto));

        var result = await Controller(grants.Object).Create(
            labelId, new CreateLabelGrantDto("user", "editor", null));

        var created = result.Result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Update_maps_optimistic_conflict_to_409()
    {
        var grants = new Mock<ILabelGrantService>();
        grants.Setup(service => service.UpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateLabelGrantDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabelGrantMutationResult(LabelGrantMutationStatus.Conflict));

        var result = await Controller(grants.Object).Update(
            Guid.NewGuid(), Guid.NewGuid(), new UpdateLabelGrantDto("editor", null, 1));

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Revoke_requires_version_and_returns_no_content_on_success()
    {
        var grants = new Mock<ILabelGrantService>();
        grants.Setup(service => service.RevokeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabelGrantMutationResult(LabelGrantMutationStatus.Success));
        var controller = Controller(grants.Object);

        (await controller.Revoke(Guid.NewGuid(), Guid.NewGuid(), null))
            .Should().BeOfType<BadRequestObjectResult>();
        (await controller.Revoke(Guid.NewGuid(), Guid.NewGuid(), 1))
            .Should().BeOfType<NoContentResult>();
    }

    [Theory]
    [InlineData(null, 0, 20)]
    [InlineData("ab", 0, 20)]
    [InlineData("alice", -1, 20)]
    [InlineData("alice", 101, 20)]
    [InlineData("alice", 0, 21)]
    public async Task Share_options_reject_invalid_bounded_search(
        string? search,
        int first,
        int max)
    {
        var options = new Mock<ILabelShareOptionsService>(MockBehavior.Strict);

        var result = await Controller(options: options.Object)
            .Options(Guid.NewGuid(), search, first, max);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        options.VerifyNoOtherCalls();
    }

    [Fact]
    public void Share_options_uses_the_bounded_directory_rate_limit()
    {
        var attribute = typeof(LabelGrantsController).GetMethod(nameof(LabelGrantsController.Options))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        attribute.PolicyName.Should().Be("share-options");
    }

    private static LabelGrantsController Controller(
        ILabelGrantService? grants = null,
        ILabelShareOptionsService? options = null) =>
        new(grants ?? Mock.Of<ILabelGrantService>(), options ?? Mock.Of<ILabelShareOptionsService>());

    private static IReadOnlyList<ResponseContract> ResponseContracts(string actionName) =>
        typeof(LabelGrantsController).GetMethod(actionName)!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), true)
            .Cast<ProducesResponseTypeAttribute>()
            .Select(attribute => new ResponseContract(attribute.StatusCode, attribute.Type ?? typeof(void)))
            .ToList();

    private sealed record ResponseContract(int StatusCode, Type ResponseType);
}
