using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using AuditNode.API.Security;
using Microsoft.AspNetCore.DataProtection;

namespace AuditNode.Tests.Services;

public sealed class GlobalCatalogDependencyInjectionTests
{
    [Fact]
    public void Global_catalog_read_graph_resolves_with_scoped_repository_and_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<ICurrentUserService>());
        services.AddDbContext<AuditDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<ICatalogCursorProtector>(new DataProtectionCatalogCursorProtector(new EphemeralDataProtectionProvider()));
        services.AddSingleton<ICatalogCursorCodec, CatalogCursorCodec>();
        services.AddScoped<IGlobalCatalogRepository, GlobalCatalogRepository>();
        services.AddScoped<ILabelCatalogService, LabelCatalogService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IGlobalCatalogRepository>().Should().BeOfType<GlobalCatalogRepository>();
        scope.ServiceProvider.GetRequiredService<ILabelCatalogService>().Should().BeOfType<LabelCatalogService>();
        provider.GetRequiredService<ICatalogCursorCodec>().Should().BeOfType<CatalogCursorCodec>();
    }

    [Fact]
    public void Missing_cursor_protector_fails_service_graph_validation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICatalogCursorCodec, CatalogCursorCodec>();

        Action build = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        build.Should().Throw<AggregateException>()
            .WithMessage("*ICatalogCursorProtector*");
    }
}
