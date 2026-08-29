using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AuditNode.Tests.Services;

public sealed class LabelPolicyDependencyInjectionTests
{
    [Fact]
    public void Scoped_label_policy_graph_resolves_with_scope_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<ITenantProvider>());
        services.AddSingleton(Mock.Of<ICurrentUserService>());
        services.AddSingleton(Mock.Of<IIdentityAdminService>());
        services.AddDbContext<AuditDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<ILabelAccessService, LabelAccessService>();
        services.AddScoped<ILabelGrantService, LabelGrantService>();
        services.AddScoped<IShareTokenService, ShareTokenService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILabelAccessService>().Should().BeOfType<LabelAccessService>();
        scope.ServiceProvider.GetRequiredService<ILabelGrantService>().Should().BeOfType<LabelGrantService>();
        scope.ServiceProvider.GetRequiredService<IShareTokenService>().Should().BeOfType<ShareTokenService>();
    }
}
