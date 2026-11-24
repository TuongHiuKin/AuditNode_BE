using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AuditNode.API.IntegrationTests.Fixtures;

public sealed class AuditNodeWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? _connectionString;

    public AuditNodeWebApplicationFactory()
    {
    }

    internal AuditNodeWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public TestJwtIssuer Tokens { get; } = new();
    public FakeIdentityServices Identities { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = _connectionString ?? TestDatabase.RequireConnectionString();
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Identity:Issuer"] = TestJwtIssuer.Issuer,
                ["Identity:Audience"] = TestJwtIssuer.Audience,
                ["Identity:SigningKey"] = TestJwtIssuer.SigningKey,
                ["Cors:AllowedOrigins:0"] = "https://allowed.auditnode.test"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AuditDbContext>();
            services.RemoveAll<DbContextOptions<AuditDbContext>>();
            services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(connectionString));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.Configure<CorsOptions>(options => options.AddPolicy("AllowReact", policy => policy
                .WithOrigins("https://allowed.auditnode.test")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
            services.RemoveAll<IIdentityAuthService>();
            services.RemoveAll<IIdentityAdminService>();
            services.AddSingleton(Identities);
            services.AddSingleton<IIdentityAuthService>(Identities);
            services.AddSingleton<IIdentityAdminService>(Identities);
        });
    }

    public HttpClient CreateHttpsClient(bool allowAutoRedirect = false) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = allowAutoRedirect,
        HandleCookies = true
    });

    public async Task InitializeAsync()
    {
        _ = Server;
        await TestDatabase.MigrateAsync(Services);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Tokens.Dispose();
        await base.DisposeAsync();
    }
}
