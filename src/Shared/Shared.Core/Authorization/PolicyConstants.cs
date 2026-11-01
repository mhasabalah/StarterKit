namespace Shared.Core.Authorization;

/// <summary>
/// Authorization policy name constants — defined in Identity module, used across all modules.
/// Use these instead of magic strings to get compile-time safety when referencing policies.
/// </summary>
public static class PolicyConstants
{
    public const string UserOnly = "UserOnly";
    public const string AdminOnly = "AdminOnly";
    public const string SuperAdminOnly = "SuperAdminOnly";
    public const string ProfileComplete = "ProfileComplete";
}
