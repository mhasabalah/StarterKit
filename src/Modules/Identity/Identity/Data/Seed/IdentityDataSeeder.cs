using Identity.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Data.Seed;

public class IdentityDataSeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<IdentitySecurityOptions> securityOptions,
    IHostEnvironment environment,
    ILogger<IdentityDataSeeder> logger) : IDataSeeder
{
    public Type DbContextType => typeof(ApplicationIdentityDbContext);

    public async Task SeedAllAsync()
    {
        // Roles must exist before the SuperAdmin user so AddToRoleAsync succeeds.
        await SeedRolesAsync();
        await SeedSuperAdminAsync();
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in InitialData.Roles)
        {
            if (await roleManager.FindByNameAsync(role.Name!) is not null)
            {
                continue;
            }

            var result = await roleManager.CreateAsync(role);
            if (result.Succeeded)
            {
                logger.LogInformation("Role {RoleName} created", role.Name);
            }
            else
            {
                logger.LogError("Failed to create role {RoleName}: {Errors}",
                    role.Name, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private async Task SeedSuperAdminAsync()
    {
        var seed = securityOptions.Value.SeedAdmin;

        if (await userManager.FindByIdAsync(InitialData.SuperAdminSeed.Id.ToString()) is not null ||
            await userManager.FindByEmailAsync(seed.Email) is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(seed.Password))
        {
            if (environment.IsDevelopmentLike())
            {
                logger.LogWarning(
                    "SuperAdmin not seeded: set Identity:SeedAdmin:Password (e.g. user secrets) to create it");
                return;
            }

            throw new InvalidOperationException(
                "Identity:SeedAdmin:Password must be configured (Docker secret) to seed the initial SuperAdmin.");
        }

        var user = new ApplicationUser
        {
            Id = InitialData.SuperAdminSeed.Id,
            UserName = seed.UserName,
            Email = seed.Email,
            FirstName = InitialData.SuperAdminSeed.FirstName,
            LastName = InitialData.SuperAdminSeed.LastName,
            EmailConfirmed = true,
            IsActive = true,
            IsPhoneNumberVerified = false,
            IsProfileComplete = true,
            MustChangePasswordOnNextLogin = true,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, seed.Password);
        if (!createResult.Succeeded)
        {
            logger.LogError("Failed to create SuperAdmin user: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleConstants.SuperAdmin);
        if (!roleResult.Succeeded)
        {
            logger.LogError("Failed to assign SuperAdmin role to seeded user: {Errors}",
                string.Join(", ", roleResult.Errors.Select(e => e.Description)));
            return;
        }

        // Never log the password. The account must change it at first login (MustChangePasswordOnNextLogin).
        logger.LogWarning("SuperAdmin {UserId} seeded; password change is required at first login", user.Id);
    }
}
