#if (TENANT_DATA)
using Shared.Core.MultiTenancy.Authorization;
#else
using Shared.Core.Authorization;
#endif

namespace ModuleName.Domains.Items.Features;

/// <summary>Route, tag and authorization shared by the Item endpoints.</summary>
internal static class ItemRoutes
{
    public const string Base = EndpointConfig.BaseApiPath + "/modulename/items";
    public const string Tag = "ModuleName";

    /// <summary>
    /// Every endpoint declares its authorization explicitly (architecture test). The example keeps it simple; give the
    /// module its own permission with the add-permission skill and use it here.
    /// </summary>
    public static TBuilder RequireItemsAccess<TBuilder>(this TBuilder builder, bool write)
        where TBuilder : IEndpointConventionBuilder
    {
#if (TENANT_DATA)
        // Tenant data: any member of the resolved tenant (single-tenant mode: any signed-in user). For writes that need
        // more, add a tenant permission (add-permission skill) and use .RequireTenantPermission(...) when write is true.
        _ = write;
        return builder.RequireTenantMember();
#else
        // Platform data: everyone signed in reads, admins write.
        return write ? builder.RequireAuthorization(PolicyConstants.AdminOnly) : builder.RequireAuthorization();
#endif
    }
}
