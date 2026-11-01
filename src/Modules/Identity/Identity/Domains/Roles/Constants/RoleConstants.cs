namespace Identity.Domains.Roles.Constants;

/// <summary>
/// Role name constants for consistent role management
/// </summary>
public static class RoleConstants
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    public static class RoleIds
    {
        public static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Admin = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid SuperAdmin = Guid.Parse("44444444-4444-4444-4444-444444444444");
    }

    public static class RoleDescriptions
    {
        public const string User = "Default role for all registered users";
        public const string Admin = "System administrators with full access";
        public const string SuperAdmin = "Super administrators with unrestricted access";
    }

    /// <summary>
    /// Get all available roles
    /// </summary>
    public static IEnumerable<string> GetAllRoles()
    {
        return new[] { User, Admin, SuperAdmin };
    }

    /// <summary>
    /// Check if a role name is valid
    /// </summary>
    public static bool IsValidRole(string roleName)
        => GetAllRoles().Contains(roleName, StringComparer.OrdinalIgnoreCase);
}
