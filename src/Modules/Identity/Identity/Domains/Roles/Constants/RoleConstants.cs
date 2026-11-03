namespace Identity.Domains.Roles.Constants;

/// <summary>
/// Role name constants for consistent role management.
/// Keep in sync with <c>Shared.Core.Authorization.RoleConstants</c> (enforced by a test).
/// What each role may do is defined in <see cref="Shared.Core.Authorization.RolePermissionMap"/>.
/// </summary>
public static class RoleConstants
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string Finance = "Finance";
    public const string Support = "Support";

    public static class RoleIds
    {
        public static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Admin = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public static readonly Guid SuperAdmin = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public static readonly Guid Finance = Guid.Parse("66666666-6666-6666-6666-666666666666");
        public static readonly Guid Support = Guid.Parse("77777777-7777-7777-7777-777777777777");
    }

    public static class RoleDescriptions
    {
        public const string User = "Default role for all registered users";
        public const string Admin = "System administrators with full access";
        public const string SuperAdmin = "Super administrators with unrestricted access";
        public const string Finance = "Back-office finance staff (billing, plans, payment settings, audit)";
        public const string Support = "Back-office support staff (read-only billing and user lookups)";
    }

    /// <summary>Back-office roles. Granting or revoking one of them is a SuperAdmin-only operation.</summary>
    public static IReadOnlyList<string> StaffRoles { get; } = [Admin, SuperAdmin, Finance, Support];

    /// <summary>
    /// Get all available roles
    /// </summary>
    public static IEnumerable<string> GetAllRoles()
    {
        return new[] { User, Admin, SuperAdmin, Finance, Support };
    }

    /// <summary>
    /// Check if a role name is valid
    /// </summary>
    public static bool IsValidRole(string roleName)
        => GetAllRoles().Contains(roleName, StringComparer.OrdinalIgnoreCase);

    public static bool IsStaffRole(string roleName) => StaffRoles.Contains(roleName);
}
