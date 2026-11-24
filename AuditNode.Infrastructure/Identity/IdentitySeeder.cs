using AuditNode.Infrastructure.Data;
using AuditNode.Infrastructure.Identity;
using AuditNode.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuditNode.Infrastructure.Identity;

/// <summary>
/// Creates the very first SystemAdmin. Without it a fresh database has no way in:
/// registration only creates plain users, and only an administrator can grant roles.
///
/// Credentials come from <c>Identity:BootstrapAdmin:Username/Email/Password</c>.
/// When those settings are absent the seeder is a no-op, so ordinary deployments
/// are unaffected.
/// </summary>
public static class IdentitySeeder
{
    public const string UsernameKey = "Identity:BootstrapAdmin:Username";
    public const string EmailKey = "Identity:BootstrapAdmin:Email";
    public const string PasswordKey = "Identity:BootstrapAdmin:Password";

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var username = configuration[UsernameKey];
        var password = configuration[PasswordKey];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return;

        var users = services.GetRequiredService<UserManager<AppUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var database = services.GetRequiredService<AuditDbContext>();

        // Serialise concurrent starts; the advisory lock is released with the connection.
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_lock(918273645)", cancellationToken);
        try
        {
            if (!await roles.RoleExistsAsync(IdentityAuthService.SystemAdminRole))
                await roles.CreateAsync(new IdentityRole(IdentityAuthService.SystemAdminRole));

            var existing = await users.FindByNameAsync(username);
            if (existing is null)
            {
                var user = new AppUser
                {
                    UserName = username,
                    Email = configuration[EmailKey],
                    EmailConfirmed = true,
                };
                var created = await users.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    logger.LogError(
                        "Bootstrap administrator {Username} could not be created: {Errors}",
                        username,
                        string.Join("; ", created.Errors.Select(error => error.Description)));
                    return;
                }
                existing = user;
                logger.LogInformation("Bootstrap administrator {Username} created.", username);
            }

            if (!await users.IsInRoleAsync(existing, IdentityAuthService.SystemAdminRole))
            {
                await users.AddToRoleAsync(existing, IdentityAuthService.SystemAdminRole);
                logger.LogInformation(
                    "Bootstrap administrator {Username} granted {Role}.",
                    username,
                    IdentityAuthService.SystemAdminRole);
            }
        }
        finally
        {
            await database.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock(918273645)", CancellationToken.None);
        }
    }
}
