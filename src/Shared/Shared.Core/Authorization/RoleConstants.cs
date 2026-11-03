namespace Shared.Core.Authorization;

/// <summary>
/// Shared role name constants — matches the seed roles defined in Identity module.
/// Use these in handlers and policies across all modules instead of magic strings.
/// Prefer permissions (<see cref="Permissions"/>) over role checks for admin features.
/// </summary>
public static class RoleConstants
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Back-office finance staff: billing, plans, payment settings, audit (see RolePermissionMap).</summary>
    public const string Finance = "Finance";

    /// <summary>Back-office support staff: read-only billing and user lookups.</summary>
    public const string Support = "Support";
}
