namespace Shared.Core.Authorization;

/// <summary>
/// Shared role name constants — matches the seed roles defined in Identity module.
/// Use these in handlers and policies across all modules instead of magic strings.
/// </summary>
public static class RoleConstants
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
}
