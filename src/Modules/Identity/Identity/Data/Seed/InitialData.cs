using Identity.Domains.Roles.Constants;

namespace Identity.Data.Seed;

public static class InitialData
{
    public static IEnumerable<ApplicationRole> Roles => new List<ApplicationRole>
    {
        new ApplicationRole(
            RoleConstants.RoleIds.User,
            RoleConstants.User,
            RoleConstants.RoleDescriptions.User
        ),
        new ApplicationRole(
            RoleConstants.RoleIds.Admin,
            RoleConstants.Admin,
            RoleConstants.RoleDescriptions.Admin
        ),
        new ApplicationRole(
            RoleConstants.RoleIds.SuperAdmin,
            RoleConstants.SuperAdmin,
            RoleConstants.RoleDescriptions.SuperAdmin
        )
    };

    /// <summary>
    /// Seed data for the initial SuperAdmin account.
    /// ⚠️ Change the password immediately after first login.
    /// </summary>
    public static class SuperAdminSeed
    {
        public static readonly Guid Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public const string Email    = "admin@starterkit.local";
        public const string UserName = "superadmin";
        public const string FirstName = "Super";
        public const string LastName  = "Admin";

        // ⚠️ CHANGE THIS PASSWORD AFTER FIRST LOGIN
        public const string Password = "Sup3rAdm!n@Starter#";
    }
}
