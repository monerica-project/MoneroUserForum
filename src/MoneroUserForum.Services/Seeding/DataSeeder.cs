using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Services.Seeding;

/// <summary>
/// Ensures the two roles exist and creates the initial admin account (approved,
/// in Admin + Member roles) from configuration on first run.
/// Config keys: Seed:AdminUsername, Seed:AdminPassword.
/// </summary>
public class DataSeeder
{
    private readonly UserManager<AppUser> users;
    private readonly RoleManager<AppRole> roles;
    private readonly IConfiguration config;
    private readonly ILogger<DataSeeder> logger;

    public DataSeeder(UserManager<AppUser> users, RoleManager<AppRole> roles, IConfiguration config, ILogger<DataSeeder> logger)
    {
        this.users = users;
        this.roles = roles;
        this.config = config;
        this.logger = logger;
    }

    public async Task SeedAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await this.roles.RoleExistsAsync(role))
            {
                await this.roles.CreateAsync(new AppRole(role));
            }
        }

        var username = this.config["Seed:AdminUsername"];
        var password = this.config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var normalized = username.Trim().ToLowerInvariant();
        var existing = await this.users.FindByNameAsync(normalized);
        if (existing is not null)
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = normalized,
            DisplayName = username.Trim(),
            IsApproved = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        var result = await this.users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            this.logger.LogError("Admin seed failed: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await this.users.AddToRolesAsync(admin, new[] { Roles.Admin, Roles.Member });
        this.logger.LogInformation("Seeded admin account '{User}'.", normalized);
    }
}
