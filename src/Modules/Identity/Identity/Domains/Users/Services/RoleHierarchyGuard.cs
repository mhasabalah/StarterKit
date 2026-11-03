namespace Identity.Domains.Users.Services;

/// <summary>
/// Who may manage whom through the admin endpoints:
/// <list type="bullet">
/// <item>SuperAdmin may manage anyone except themselves.</item>
/// <item>Admin may manage only plain users (never staff: Admin, SuperAdmin, Finance or Support).</item>
/// <item>Nobody manages their own account through admin endpoints (self-service endpoints exist for that).</item>
/// <item>Everyone else may manage nobody.</item>
/// </list>
/// </summary>
public static class RoleHierarchy
{
    public static bool IsSuperAdmin(IEnumerable<string> roles) => roles.Contains(RoleConstants.SuperAdmin);

    public static bool IsAdminOrAbove(IEnumerable<string> roles) =>
        roles.Any(r => r is RoleConstants.Admin or RoleConstants.SuperAdmin);

    /// <summary>Any back-office role (<see cref="RoleConstants.StaffRoles"/>).</summary>
    public static bool IsStaff(IEnumerable<string> roles) => roles.Any(RoleConstants.IsStaffRole);

    public static bool CanManage(
        Guid callerId, IReadOnlyCollection<string> callerRoles, Guid targetId, IReadOnlyCollection<string> targetRoles)
    {
        if (callerId == targetId)
        {
            return false;
        }

        if (IsSuperAdmin(callerRoles))
        {
            return true;
        }

        return callerRoles.Contains(RoleConstants.Admin) && !IsStaff(targetRoles);
    }
}

/// <summary>
/// Enforces <see cref="RoleHierarchy"/> with roles loaded from the database (never from possibly stale claims).
/// Must be called by EVERY user/admin mutation handler before it changes anything.
/// </summary>
public interface IRoleHierarchyGuard
{
    /// <summary>Throws <see cref="ForbiddenException"/> unless <paramref name="callerId"/> may manage <paramref name="target"/>.</summary>
    Task EnsureCanManageAsync(Guid callerId, ApplicationUser target, CancellationToken ct = default);

    /// <summary>Throws <see cref="ForbiddenException"/> unless <paramref name="callerId"/> is an active SuperAdmin.</summary>
    Task EnsureIsSuperAdminAsync(Guid callerId, CancellationToken ct = default);
}

public sealed class RoleHierarchyGuard(
    UserManager<ApplicationUser> userManager,
    ILogger<RoleHierarchyGuard> logger) : IRoleHierarchyGuard
{
    public const string ForbiddenMessage = "You are not allowed to manage this account.";

    public async Task EnsureCanManageAsync(Guid callerId, ApplicationUser target, CancellationToken ct = default)
    {
        var callerRoles = await GetActiveCallerRolesAsync(callerId);
        var targetRoles = (await userManager.GetRolesAsync(target)).ToList();

        if (!RoleHierarchy.CanManage(callerId, callerRoles, target.Id, targetRoles))
        {
            logger.LogWarning(
                "Role hierarchy violation: caller {CallerId} ({CallerRoles}) tried to manage {TargetId} ({TargetRoles})",
                callerId, string.Join(",", callerRoles), target.Id, string.Join(",", targetRoles));
            throw new ForbiddenException(ForbiddenMessage);
        }
    }

    public async Task EnsureIsSuperAdminAsync(Guid callerId, CancellationToken ct = default)
    {
        var callerRoles = await GetActiveCallerRolesAsync(callerId);
        if (!RoleHierarchy.IsSuperAdmin(callerRoles))
        {
            logger.LogWarning("Caller {CallerId} is not a SuperAdmin", callerId);
            throw new ForbiddenException("Only a SuperAdmin can perform this operation.");
        }
    }

    private async Task<List<string>> GetActiveCallerRolesAsync(Guid callerId)
    {
        var caller = await userManager.FindByIdAsync(callerId.ToString());
        if (caller is null || !caller.IsActive)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        return (await userManager.GetRolesAsync(caller)).ToList();
    }
}
