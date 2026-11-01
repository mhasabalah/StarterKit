using Identity.Domains.Roles.Constants;

namespace Identity.Data.Seed;

public class IdentityDataSeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    ILogger<IdentityDataSeeder> logger) : IDataSeeder
{
    public Type DbContextType => typeof(ApplicationIdentityDbContext);

    public async Task SeedAllAsync()
    {
        logger.LogInformation("Seeding Identity data...");

        // Roles must be seeded before the SuperAdmin user so AddToRoleAsync succeeds.
        await SeedRolesAsync();
        await SeedSuperAdminAsync();

        logger.LogInformation("Identity data seeding completed");
    }

    private async Task SeedRolesAsync()
    {
        logger.LogInformation("Checking roles...");

        foreach (var role in InitialData.Roles)
        {
            var existingRole = await roleManager.FindByNameAsync(role.Name!);
            if (existingRole == null)
            {
                logger.LogInformation("Creating role: {RoleName}", role.Name);
                var result = await roleManager.CreateAsync(role);

                if (result.Succeeded)
                {
                    logger.LogInformation("Role {RoleName} created successfully", role.Name);
                }
                else
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    logger.LogError("Failed to create role {RoleName}: {Errors}", role.Name, errors);
                }
            }
            else
            {
                logger.LogInformation("Role {RoleName} already exists", role.Name);
            }
        }

        logger.LogInformation("Roles seeding completed");
    }

    private async Task SeedSuperAdminAsync()
    {
        var existingUser = await userManager.FindByEmailAsync(InitialData.SuperAdminSeed.Email);
        if (existingUser != null)
        {
            logger.LogInformation("SuperAdmin user already exists, skipping");
            return;
        }

        var user = new ApplicationUser
        {
            Id                    = InitialData.SuperAdminSeed.Id,
            UserName              = InitialData.SuperAdminSeed.UserName,
            Email                 = InitialData.SuperAdminSeed.Email,
            NormalizedUserName    = InitialData.SuperAdminSeed.UserName.ToUpperInvariant(),
            NormalizedEmail       = InitialData.SuperAdminSeed.Email.ToUpperInvariant(),
            FirstName             = InitialData.SuperAdminSeed.FirstName,
            LastName              = InitialData.SuperAdminSeed.LastName,
            EmailConfirmed        = true,
            IsActive              = true,
            IsPhoneNumberVerified = false,
            IsProfileComplete     = true,
            CreatedAt             = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, InitialData.SuperAdminSeed.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to create SuperAdmin user: {Errors}", errors);
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleConstants.SuperAdmin);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to assign SuperAdmin role to seeded user: {Errors}", errors);
            return;
        }

        // Log credentials once so the operator knows them — change immediately after first login.
        logger.LogWarning(
            "SuperAdmin seeded successfully. Email: {Email} | Password: {Password} | " +
            "⚠️  CHANGE THIS PASSWORD IMMEDIATELY AFTER FIRST LOGIN.",
            InitialData.SuperAdminSeed.Email,
            InitialData.SuperAdminSeed.Password);
    }
}
