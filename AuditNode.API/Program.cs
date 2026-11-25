using AuditNode.Application.Interfaces;
using AuditNode.Application.Services;
using AuditNode.Domain.Entities;
using AuditNode.Infrastructure.Identity;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;

using AuditNode.Application.Validators;
using Microsoft.EntityFrameworkCore;
using FluentValidation.AspNetCore;
using FluentValidation;
using Scalar.AspNetCore;
using AuditNode.API.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using AuditNode.API.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDataProtection();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "AuditNode API";
        document.Info.Version = "v1";
        document.Info.Description = "Infrastructure Audit, Port & Monitoring API Gateway";
        return Task.CompletedTask;
    });
});

// Configure JWT Bearer Authentication against locally issued tokens.
// Access tokens are signed by this API (see IdentityTokenIssuer) with the
// Identity:SigningKey symmetric key; validation uses the same options object.
builder.Services.AddOptions<IdentityRuntimeOptions>()
    .Bind(builder.Configuration.GetSection("Identity"))
    .Validate(options => options.IsValid(),
        "Identity:Issuer, Identity:Audience and a SigningKey of at least 32 bytes are required.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<IdentityRuntimeOptions>>((jwt, identity) =>
    {
        var key = identity.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(key)) return; // validated at startup

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = identity.Value.Issuer,
            ValidAudience = identity.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name,
        };
        jwt.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context => {
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuditNode.Security.JwtBearer")
                    .LogWarning(context.Exception, "Token validation failed for {Path}", context.Request.Path);
                return Task.CompletedTask;
            },
            OnChallenge = context => {
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuditNode.Security.JwtBearer")
                    .LogInformation(
                        "Rejected {Path} with {StatusCode}: {Error} {Description}",
                        context.Request.Path,
                        StatusCodes.Status401Unauthorized,
                        context.Error,
                        context.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options => options.AddPolicy("SystemAdminOnly", policy => policy.RequireRole("SystemAdmin")));
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
});

// Full-stack E2E signs in as six actors from one address, so the login budget has to
// be configurable. Absent configuration the production value of 20 applies.
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AuditNode.Security.RateLimit");
        logger.LogWarning(
            "Rate limit rejected request {Path} for actor {ActorId}; correlation {CorrelationId}",
            context.HttpContext.Request.Path,
            context.HttpContext.User.FindFirst("sub")?.Value ?? "anonymous",
            context.HttpContext.TraceIdentifier);
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                // Overridable so a full-stack suite that signs in as several actors from
                // one address does not exhaust the budget mid-run. Production keeps 20.
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("share-options", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst("sub")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("share-link-create", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst("sub")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("share-link-resolve", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("share-link-browse", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 10;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AuditDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<ISystemAdminMutationLock, PostgresSystemAdminMutationLock>();
builder.Services.AddScoped<IIdentityTokenIssuer, IdentityTokenIssuer>();
builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
builder.Services.AddScoped<IdentityAuthService>();
builder.Services.AddScoped<IIdentityAuthService>(provider => provider.GetRequiredService<IdentityAuthService>());
builder.Services.AddScoped<IIdentityAdminService>(provider => provider.GetRequiredService<IdentityAuthService>());

// Register DbContext with PostgreSQL provider
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseReadyHealthCheck>("database", tags: ["ready"]);

// Register Repositories
builder.Services.AddScoped<IServerRepository, ServerRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
builder.Services.AddScoped<ITopologyRepository, TopologyRepository>();
builder.Services.AddScoped<IDatacenterRepository, DatacenterRepository>();
builder.Services.AddScoped<IGlobalCatalogRepository, GlobalCatalogRepository>();
builder.Services.AddScoped<IOwnerLabelService, OwnerLabelService>();
builder.Services.AddSingleton<ICatalogCursorProtector, DataProtectionCatalogCursorProtector>();
builder.Services.AddSingleton<ICatalogCursorCodec, CatalogCursorCodec>();

// Register Services
builder.Services.AddScoped<IApplicationService, AuditNode.Infrastructure.Services.ApplicationService>();
builder.Services.AddScoped<IServerService, AuditNode.Infrastructure.Services.ServerService>();
builder.Services.AddScoped<IDatacenterService, AuditNode.Infrastructure.Services.DatacenterService>();
builder.Services.AddScoped<IDependencyService, AuditNode.Infrastructure.Services.DependencyService>();
builder.Services.AddScoped<IExternalServiceService, ExternalServiceService>();
builder.Services.AddScoped<ITopologyCommandService, TopologyCommandService>();
builder.Services.AddScoped<IInventoryImportService, AuditNode.Infrastructure.Services.InventoryImportService>();
builder.Services.AddScoped<IInventorySearchService, AuditNode.Infrastructure.Services.InventorySearchService>();
builder.Services.AddScoped<ILabelCatalogService, LabelCatalogService>();
builder.Services.AddScoped<IShareCatalogService, ShareCatalogService>();
builder.Services.AddScoped<IInfrastructureService, AuditNode.Infrastructure.Services.InfrastructureService>();
builder.Services.AddScoped<ILabelAccessService, LabelAccessService>();
builder.Services.AddScoped<IOwnerGraphAccessService, OwnerGraphAccessService>();
builder.Services.AddScoped<ILabelMutationCoordinator, LabelMutationCoordinator>();
builder.Services.AddScoped<ILabelGrantService, LabelGrantService>();
builder.Services.AddScoped<IShareTokenService, ShareTokenService>();
builder.Services.AddScoped<ILabelShareOptionsService, LabelShareOptionsService>();
builder.Services.AddScoped<IAnalyticsService, AuditNode.Application.Services.AnalyticsService>();
builder.Services.AddScoped<ITopologyQueryService, AuditNode.Application.Services.TopologyQueryService>();

// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateServerDtoValidator>();

// Add CORS policy
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:3000"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policyBuilder =>
    {
        policyBuilder
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Add controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var app = builder.Build();

await using (var startupScope = app.Services.CreateAsyncScope())
{
    var database = startupScope.ServiceProvider.GetRequiredService<AuditDbContext>();

    if (args.Contains("--migrate-only", StringComparer.Ordinal))
    {
        await database.Database.MigrateAsync();
        return;
    }

    // The bootstrap administrator is a first-run convenience: it only exists when
    // explicit credentials are configured. In that case the schema is created too,
    // because otherwise the seeder has no tables to write to.
    var hasBootstrapAdmin =
        !string.IsNullOrWhiteSpace(app.Configuration[IdentitySeeder.UsernameKey]) &&
        !string.IsNullOrWhiteSpace(app.Configuration[IdentitySeeder.PasswordKey]);

    if (hasBootstrapAdmin)
    {
        await database.Database.MigrateAsync();

        await IdentitySeeder.SeedAsync(
            startupScope.ServiceProvider,
            app.Configuration,
            startupScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AuditNode.Identity"));
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("AuditNode API Reference")
               .WithTheme(ScalarTheme.Moon)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    await next();
});
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseRouting();

// Enable CORS early in the pipeline
app.UseCors("AllowReact");

// Authentication MUST run before Authorization
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Deliberately anonymous: probes expose only process/dependency availability.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

// Map controllers
app.MapControllers();

app.Run();

public sealed class DatabaseReadyHealthCheck(AuditDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
            return HealthCheckResult.Unhealthy("Database is unavailable.");

        var requiredViews = await database.Database.SqlQueryRaw<string>(
                """
                SELECT viewname AS "Value"
                FROM pg_catalog.pg_views
                WHERE schemaname = 'public'
                  AND viewname IN ('v_topology_map', 'v_dependency_graph')
                """)
            .ToListAsync(cancellationToken);

        return requiredViews.Count == 2
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Required database views are unavailable.");
    }
}

public partial class Program;
