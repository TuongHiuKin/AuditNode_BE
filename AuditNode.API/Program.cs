using AuditNode.Application.Interfaces;
using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using FluentValidation.AspNetCore;
using AuditNode.Application.Validators;
using FluentValidation;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<IApplicationService, AuditNode.Application.Services.ApplicationService>();
builder.Services.AddScoped<IServerService, AuditNode.Application.Services.ServerService>();
builder.Services.AddScoped<IInventoryImportService, AuditNode.Infrastructure.Services.InventoryImportService>();
builder.Services.AddScoped<IInventorySearchService, AuditNode.Infrastructure.Services.InventorySearchService>();

// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateServerDtoValidator>();

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policyBuilder =>
    {
        policyBuilder
            .WithOrigins("http://localhost:5173", "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Add controllers
builder.Services.AddAuthorization();
builder.Services.AddControllers()
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

app.UseHttpsRedirection();

app.UseAuthorization();

// Enable CORS
app.UseCors("AllowReact");

// Map controllers
app.MapControllers();

app.Run();
