namespace Catalog.Common;

/// <summary>Endpoint-level helper for "public read, extra data for admins" lookups.</summary>
internal static class LookupAccess
{
    public static bool IsAdmin(HttpContext http) =>
        http.User.IsInRole(RoleConstants.Admin) || http.User.IsInRole(RoleConstants.SuperAdmin);
}
