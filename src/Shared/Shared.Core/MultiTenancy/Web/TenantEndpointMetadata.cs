using Microsoft.AspNetCore.Builder;

namespace Shared.Core.MultiTenancy.Web;

/// <summary>How an endpoint relates to tenants (read by <see cref="TenantResolutionMiddleware"/>).</summary>
public enum TenantRequirement
{
    /// <summary>
    /// Tenant is used when present; an invalid one is ignored. Default for endpoints without metadata OUTSIDE
    /// tenant-data modules. Data access still fails closed.
    /// </summary>
    Optional = 0,

    /// <summary>
    /// A resolved, serviceable tenant is mandatory: 400 <c>tenant_required</c> otherwise. Default for endpoints
    /// declared in an assembly that owns an <see cref="ITenantDataContext"/> (Catalog, Attachment, new modules).
    /// </summary>
    Required = 1,

    /// <summary>Platform (control-plane) endpoint: never runs inside a tenant, even if one is sent.</summary>
    Host = 2
}

public sealed record TenantRequirementMetadata(TenantRequirement Requirement);

public static class TenantEndpointExtensions
{
    /// <summary>The endpoint/group works on tenant data: a tenant must be resolved.</summary>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TenantRequirementMetadata(TenantRequirement.Required));

    /// <summary>Opts an endpoint of a tenant-data module out of the default <see cref="TenantRequirement.Required"/>.</summary>
    public static TBuilder TenantOptional<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TenantRequirementMetadata(TenantRequirement.Optional));

    /// <summary>The endpoint/group is a platform (host) endpoint.</summary>
    public static TBuilder HostOnly<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new TenantRequirementMetadata(TenantRequirement.Host));
}
