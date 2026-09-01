using AuditNode.API.Controllers;
using AuditNode.Application.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Reflection;

namespace AuditNode.Tests.Controllers;

public sealed class ApiResponseContractTests
{
    [Theory]
    [InlineData(typeof(ServersController), nameof(ServersController.GetServers), typeof(CursorPageDto<ServerResponseDto>))]
    [InlineData(typeof(ApplicationsController), nameof(ApplicationsController.GetApplications), typeof(CursorPageDto<ApplicationResponseDto>))]
    [InlineData(typeof(DatacentersController), nameof(DatacentersController.GetDatacenters), typeof(CursorPageDto<DatacenterDto>))]
    [InlineData(typeof(InventorySearchController), nameof(InventorySearchController.Search), typeof(CursorPageDto<SearchResultDto>))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.GetLabels), typeof(CursorPageDto<CatalogLabelDto>))]
    public void Global_catalog_reads_publish_cursor_page_and_validation_metadata(
        Type controllerType,
        string action,
        Type responseType)
    {
        var method = controllerType.GetMethod(action)!;
        var metadata = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        metadata.Should().Contain(attribute => attribute.StatusCode == StatusCodes.Status200OK && attribute.Type == responseType);
        metadata.Should().Contain(attribute => attribute.StatusCode == StatusCodes.Status400BadRequest && attribute.Type == typeof(ProblemDetails));
    }

    [Theory]
    [InlineData(nameof(LabelGrantsController), nameof(LabelGrantsController.Create), typeof(LabelGrantDto))]
    [InlineData(nameof(ShareLinksController), nameof(ShareLinksController.Create), typeof(CreateShareLinkResponseDto))]
    public void ApiExplorer_describes_create_as_typed_201_without_an_inferred_200(
        string controller,
        string action,
        Type responseType)
    {
        var responses = Describe(controller, action);

        responses.Should().ContainSingle(response =>
            response.StatusCode == StatusCodes.Status201Created &&
            response.ResponseType == responseType);
        responses.Should().NotContain(response => response.StatusCode == StatusCodes.Status200OK);
    }

    [Fact]
    public void ApiExplorer_describes_anonymous_resolve_with_only_200_404_and_429()
    {
        var responses = Describe(nameof(ShareLinksController), nameof(ShareLinksController.Resolve));

        responses.Should().BeEquivalentTo([
            new ApiResponse(StatusCodes.Status200OK, typeof(ShareTokenResolutionDto)),
            new ApiResponse(StatusCodes.Status404NotFound, typeof(ProblemDetails)),
            new ApiResponse(StatusCodes.Status429TooManyRequests, typeof(ProblemDetails))
        ]);
    }

    private static IReadOnlyList<ApiResponse> Describe(string controller, string action)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(LabelGrantsController).Assembly);
        using var provider = services.BuildServiceProvider();
        var description = provider.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Single(item =>
                item.ActionDescriptor.RouteValues["controller"] == controller.Replace("Controller", string.Empty) &&
                item.ActionDescriptor.RouteValues["action"] == action);

        return description.SupportedResponseTypes
            .Select(response => new ApiResponse(
                response.StatusCode,
                response.Type ?? typeof(void)))
            .ToList();
    }

    private sealed record ApiResponse(int StatusCode, Type ResponseType);
}
