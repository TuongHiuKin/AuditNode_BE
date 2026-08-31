using System.Reflection;
using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using AuditNode.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Xunit;

namespace AuditNode.Tests.Controllers;

public sealed class ShareLinksControllerTests
{
    [Fact]
    public void Actions_declare_exact_success_and_failure_response_contracts()
    {
        ResponseContracts(nameof(ShareLinksController.Create)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status201Created, typeof(CreateShareLinkResponseDto)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status409Conflict, typeof(void)),
            new ResponseContract(StatusCodes.Status429TooManyRequests, typeof(void))
        ]);
        ResponseContracts(nameof(ShareLinksController.Create))
            .Should().NotContain(contract => contract.StatusCode == StatusCodes.Status200OK,
                "ApiExplorer must not infer 200 for create");
        ResponseContracts(nameof(ShareLinksController.List)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status200OK, typeof(IReadOnlyList<ShareLinkMetadataDto>)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void))
        ]);
        ResponseContracts(nameof(ShareLinksController.Revoke)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status204NoContent, typeof(void)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status409Conflict, typeof(void))
        ]);
        ResponseContracts(nameof(ShareLinksController.Resolve)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status200OK, typeof(ShareTokenResolutionDto)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status429TooManyRequests, typeof(void))
        ]);
        ResponseContracts(nameof(ShareLinksController.Browse)).Should().BeEquivalentTo([
            new ResponseContract(StatusCodes.Status200OK, typeof(CursorPageDto<ShareCatalogItemDto>)),
            new ResponseContract(StatusCodes.Status400BadRequest, typeof(void)),
            new ResponseContract(StatusCodes.Status404NotFound, typeof(void)),
            new ResponseContract(StatusCodes.Status429TooManyRequests, typeof(void))
        ]);
    }

    [Fact]
    public void Public_share_actions_are_anonymous_rate_limited_and_receive_token_only_from_body()
    {
        typeof(ShareLinksController).Should().BeDecoratedWith<AuthorizeAttribute>();
        var resolve = typeof(ShareLinksController).GetMethod(nameof(ShareLinksController.Resolve))!;
        resolve.Should().BeDecoratedWith<AllowAnonymousAttribute>();
        resolve.GetCustomAttributes<EnableRateLimitingAttribute>().Single().PolicyName
            .Should().Be("share-link-resolve");
        var tokenParameter = resolve.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(ResolveShareLinkDto));
        tokenParameter.GetCustomAttribute<FromBodyAttribute>().Should().NotBeNull();
        var browse = typeof(ShareLinksController).GetMethod(nameof(ShareLinksController.Browse))!;
        browse.Should().BeDecoratedWith<AllowAnonymousAttribute>();
        browse.GetCustomAttributes<EnableRateLimitingAttribute>().Single().PolicyName.Should().Be("share-link-browse");
        browse.GetParameters().Single(parameter => parameter.ParameterType == typeof(BrowseShareLinkDto))
            .GetCustomAttribute<FromBodyAttribute>().Should().NotBeNull();

        typeof(ShareLinksController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Should().NotContain(method => method.Name.Contains("Claim", StringComparison.OrdinalIgnoreCase));
        typeof(ShareLinksController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => attribute.Template ?? string.Empty)
            .Should().OnlyContain(template =>
                !template.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                !template.Contains("claim", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_is_authenticated_and_has_a_distinct_actor_rate_limit()
    {
        var create = typeof(ShareLinksController).GetMethod(nameof(ShareLinksController.Create))!;
        create.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        create.GetCustomAttributes<EnableRateLimitingAttribute>().Single().PolicyName
            .Should().Be("share-link-create");
    }

    [Fact]
    public async Task Create_returns_raw_token_once_in_the_creation_response()
    {
        var labelId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var tokenService = new Mock<IShareTokenService>();
        tokenService.Setup(service => service.CreateAsync(
                labelId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShareTokenMutationResult(
                ShareTokenMutationStatus.Success,
                grantId,
                "raw-token",
                DateTimeOffset.UtcNow.AddHours(1),
                1));

        var result = await new ShareLinksController(tokenService.Object, Mock.Of<IShareCatalogService>()).Create(
            labelId, new CreateShareLinkDto(DateTimeOffset.UtcNow.AddHours(1)));

        var created = result.Result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().BeOfType<CreateShareLinkResponseDto>()
            .Which.Token.Should().Be("raw-token");
    }

    [Fact]
    public async Task List_returns_safe_metadata_without_raw_tokens()
    {
        var labelId = Guid.NewGuid();
        var tokenService = new Mock<IShareTokenService>();
        tokenService.Setup(service => service.ListAsync(labelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShareLinkMetadataDto(
                Guid.NewGuid(), labelId, DateTimeOffset.UtcNow.AddHours(1), null, 1, false, null)]);

        var result = await new ShareLinksController(tokenService.Object, Mock.Of<IShareCatalogService>())
            .List(labelId);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData(ShareTokenMutationStatus.Denied, StatusCodes.Status404NotFound)]
    [InlineData(ShareTokenMutationStatus.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(ShareTokenMutationStatus.Conflict, StatusCodes.Status409Conflict)]
    public async Task Create_maps_service_failures_safely(
        ShareTokenMutationStatus status,
        int expectedStatus)
    {
        var tokenService = new Mock<IShareTokenService>();
        tokenService.Setup(service => service.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShareTokenMutationResult(status));

        var result = await new ShareLinksController(tokenService.Object, Mock.Of<IShareCatalogService>()).Create(
            Guid.NewGuid(), new CreateShareLinkDto(DateTimeOffset.UtcNow.AddHours(1)));

        result.Result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Resolve_uses_one_generic_denial_for_invalid_expired_revoked_or_missing_tokens()
    {
        var tokenService = new Mock<IShareTokenService>();
        tokenService.Setup(service => service.ResolveAsync("opaque-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShareTokenResolutionDto?)null);

        var result = await new ShareLinksController(tokenService.Object, Mock.Of<IShareCatalogService>())
            .Resolve(new ResolveShareLinkDto("opaque-token"));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Revoke_maps_conflict_to_409_and_success_to_no_content()
    {
        var tokenService = new Mock<IShareTokenService>();
        tokenService.SetupSequence(service => service.RevokeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShareTokenMutationResult(ShareTokenMutationStatus.Conflict))
            .ReturnsAsync(new ShareTokenMutationResult(ShareTokenMutationStatus.Success));
        var controller = new ShareLinksController(tokenService.Object, Mock.Of<IShareCatalogService>());

        (await controller.Revoke(Guid.NewGuid(), Guid.NewGuid(), 1))
            .Should().BeOfType<ConflictObjectResult>();
        (await controller.Revoke(Guid.NewGuid(), Guid.NewGuid(), 1))
            .Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Browse_returns_generic_not_found_for_invalid_token_and_never_accepts_token_in_url()
    {
        var catalog = new Mock<IShareCatalogService>();
        catalog.Setup(service => service.BrowseAsync(It.IsAny<BrowseShareLinkDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorPageDto<ShareCatalogItemDto>?)null);
        var controller = new ShareLinksController(Mock.Of<IShareTokenService>(), catalog.Object);

        var result = await controller.Browse(new BrowseShareLinkDto("opaque-token", "servers"));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        typeof(ShareLinksController).GetMethod(nameof(ShareLinksController.Browse))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().NotContain("token");
    }

    private static IReadOnlyList<ResponseContract> ResponseContracts(string actionName) =>
        typeof(ShareLinksController).GetMethod(actionName)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => new ResponseContract(attribute.StatusCode, attribute.Type ?? typeof(void)))
            .ToList();

    private sealed record ResponseContract(int StatusCode, Type ResponseType);
}
