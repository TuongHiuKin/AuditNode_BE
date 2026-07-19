using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using AuditNode.Infrastructure.Services;
using AuditNode.Application.Validators;
using Microsoft.EntityFrameworkCore;
using FluentValidation.AspNetCore;
using FluentValidation;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using AuditNode.API.Routing;

var builder = WebApplication.CreateBuilder(args);
var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? throw new InvalidOperationException("Keycloak:Authority must be configured.");
var allowedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? [];

// Add services to the container
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
Console.WriteLine($"[DEBUG SECURITY] Keycloak Authority Loaded: {keycloakAuthority}");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakAuthority;
        options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = keycloakAuthority,
            ValidateAudience = builder.Configuration.GetValue<bool>("Keycloak:ValidateAudience"),
            ValidAudience = builder.Configuration["Keycloak:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "realm_access.roles"
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context => {
                Console.WriteLine($"[Auth Failed] {context.Exception.Message}");
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

builder.Services.AddAuthorization();

// Register DbContext with PostgreSQL provider
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register Repositories
builder.Services.AddScoped<IServerRepository, ServerRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
builder.Services.AddScoped<ITopologyRepository, TopologyRepository>();
builder.Services.AddScoped<IDatacenterRepository, DatacenterRepository>();

// Register Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, AuditNode.API.Services.CurrentUserService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAuthService, AuditNode.Infrastructure.Services.KeycloakAuthService>();
builder.Services.AddScoped<IApplicationService, AuditNode.Infrastructure.Services.ApplicationService>();
builder.Services.AddScoped<IServerService, AuditNode.Infrastructure.Services.ServerService>();
builder.Services.AddScoped<IDatacenterService, AuditNode.Infrastructure.Services.DatacenterService>();
builder.Services.AddScoped<IDependencyService, AuditNode.Infrastructure.Services.DependencyService>();
builder.Services.AddScoped<IInventoryImportService, AuditNode.Infrastructure.Services.InventoryImportService>();
builder.Services.AddScoped<IInventorySearchService, AuditNode.Infrastructure.Services.InventorySearchService>();
builder.Services.AddScoped<IInfrastructureService, AuditNode.Infrastructure.Services.InfrastructureService>();

// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateServerDtoValidator>();

// Keep browser origins in environment-specific configuration, not source code.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policyBuilder =>
    {
        if (allowedCorsOrigins.Length > 0)
        {
            policyBuilder
                .WithOrigins(allowedCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

// Add controllers
builder.Services.AddControllers(options =>
    options.Conventions.Add(new RouteTokenTransformerConvention(new LowercaseRouteTokenTransformer())))
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var app = builder.Build();

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

if (builder.Configuration.GetValue("Https:RedirectEnabled", true))
{
    app.UseHttpsRedirection();
}

// Enable CORS early in the pipeline
app.UseCors("AllowReact");

// Inject Raw Network Debugging Middleware
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value != null && 
       (context.Request.Path.Value.Contains("bulk-import", StringComparison.OrdinalIgnoreCase) || 
        context.Request.Path.Value.Contains("import", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"[RAW NETWORK DEBUG] Path: {context.Request.Path}");
        Console.WriteLine($"[RAW NETWORK DEBUG] Content-Type: {context.Request.ContentType}");
        Console.WriteLine($"[RAW NETWORK DEBUG] Content-Length: {context.Request.ContentLength}");
    }
    await next();
});

// Authentication MUST run before Authorization
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

app.Run();

