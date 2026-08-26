using AuditNode.API.Middleware;
using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;

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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
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

// Configure JWT Bearer Authentication with Keycloak
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.RequireHttpsMetadata = false; // Set to true in production
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false, // Set to false for public client SPA tokens
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context => {
                Console.WriteLine($"[Auth Failed] {context.Exception.GetType().Name}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context => {
                Console.WriteLine("[Auth Success] Token is valid.");
                return Task.CompletedTask;
            },
            OnChallenge = context => {
                Console.WriteLine($"[Auth Challenge] 401/403 about to be sent. Error: {context.Error}, Description: {context.ErrorDescription}");
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
                PermitLimit = 20,
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
});

builder.Services.AddOptions<KeycloakRuntimeOptions>()
    .Bind(builder.Configuration.GetSection("Keycloak"))
    .Validate(options => options.IsValid(), "Required Keycloak configuration is missing.")
    .ValidateOnStart();

builder.Services.AddHttpClient(KeycloakAuthService.HttpClientName);
builder.Services.AddTransient<IKeycloakHttpClientFactory, KeycloakHttpClientFactoryAdapter>();
builder.Services.AddScoped<IIdentityAuthService, KeycloakAuthService>();
builder.Services.AddScoped<IIdentityAdminService, KeycloakAuthService>();

// Register Keycloak Role Claims Transformation for RBAC
builder.Services.AddTransient<IClaimsTransformation, KeycloakRoleClaimsTransformation>();

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
builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();

// Register Services
builder.Services.AddScoped<IApplicationService, AuditNode.Infrastructure.Services.ApplicationService>();
builder.Services.AddScoped<IServerService, AuditNode.Infrastructure.Services.ServerService>();
builder.Services.AddScoped<IWorkspaceService, AuditNode.Infrastructure.Services.WorkspaceService>();
builder.Services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
builder.Services.AddScoped<IWorkspaceSharingService, WorkspaceSharingService>();
builder.Services.AddScoped<IWorkspaceUserSummaryService, WorkspaceUserSummaryService>();
builder.Services.AddScoped<IWorkspaceShareOptionsService, WorkspaceShareOptionsService>();
builder.Services.AddScoped<IScopedResourcePolicy, ScopedResourcePolicy>();
builder.Services.AddScoped<IDatacenterService, AuditNode.Infrastructure.Services.DatacenterService>();
builder.Services.AddScoped<IDependencyService, AuditNode.Infrastructure.Services.DependencyService>();
builder.Services.AddScoped<IInventoryImportService, AuditNode.Infrastructure.Services.InventoryImportService>();
builder.Services.AddScoped<IInventorySearchService, AuditNode.Infrastructure.Services.InventorySearchService>();
builder.Services.AddScoped<IInfrastructureService, AuditNode.Infrastructure.Services.InfrastructureService>();

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

if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await database.Database.MigrateAsync();
    return;
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

// Workspace isolation middleware
app.UseMiddleware<WorkspaceMiddleware>();

// Map controllers
app.MapControllers();

app.Run();

public sealed class KeycloakHttpClientFactoryAdapter : IKeycloakHttpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public KeycloakHttpClientFactoryAdapter(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public HttpClient CreateClient() =>
        _httpClientFactory.CreateClient(KeycloakAuthService.HttpClientName);
}

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
