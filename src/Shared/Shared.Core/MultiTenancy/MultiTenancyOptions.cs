namespace Shared.Core.MultiTenancy;

/// <summary>
/// <c>MultiTenancy</c> configuration section. Disabled by default: the app then behaves as a single-tenant app
/// whose data all belongs to <see cref="TenantDefaults.DefaultTenant"/> (same schema in both modes).
/// </summary>
public sealed class MultiTenancyOptions
{
    public const string SectionName = "MultiTenancy";

    /// <summary>Master switch. <c>false</c> = single tenant (Default tenant everywhere, no tenant endpoints).</summary>
    public bool Enabled { get; set; }

    /// <summary>Isolation used for new tenants unless the creator chooses otherwise.</summary>
    public TenantIsolation DefaultIsolation { get; set; } = TenantIsolation.Shared;

    /// <summary>What suspended tenants may still do.</summary>
    public SuspendedTenantMode SuspendedTenantMode { get; set; } = SuspendedTenantMode.Block;

    public TenantResolutionOptions Resolution { get; set; } = new();

    public TenantDatabaseOptions Database { get; set; } = new();

    public TenantCacheOptions Cache { get; set; } = new();

    /// <summary>Self-service tenant creation by any authenticated user (sign-up flow).</summary>
    public bool AllowSelfServiceSignup { get; set; } = true;

    /// <summary>Maximum depth of the branch tree (HQ = depth 1).</summary>
    public int MaxBranchDepth { get; set; } = 6;

    /// <summary>Days between a deletion request and the irreversible purge.</summary>
    public int DeletionGracePeriodDays { get; set; } = 30;

    public TenantInvitationOptions Invitations { get; set; } = new();

    public TenantCorsOptions Cors { get; set; } = new();

    public TenantImpersonationOptions Impersonation { get; set; } = new();
}

/// <summary>Invitation delivery (<c>MultiTenancy:Invitations</c>).</summary>
public sealed class TenantInvitationOptions
{
    /// <summary>E-mail the invitation link (template <c>TenantInvitation</c>).</summary>
    public bool SendEmail { get; set; } = true;

    /// <summary>
    /// Frontend URL of the accept page. <c>{token}</c> is replaced by the (URL-encoded) invitation token; a relative
    /// value is appended to <c>Email:AppBaseUrl</c>.
    /// </summary>
    public string AcceptUrl { get; set; } = "/invitations/accept?token={token}";

    /// <summary>
    /// Also return the raw token in the invite response (apps that deliver invitations themselves, tests).
    /// Off by default: the token then exists only in the e-mail.
    /// </summary>
    public bool ReturnTokenInResponse { get; set; }
}

/// <summary>
/// Tenant-aware CORS (<c>MultiTenancy:Cors</c>): besides <c>Cors:AllowedOrigins</c>, browser origins of tenants are
/// allowed - <c>https://{identifier}.{FrontendRootDomain}</c> and the tenants' VERIFIED custom domains.
/// </summary>
public sealed class TenantCorsOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Root domain of tenant frontends, e.g. <c>app.example.com</c> → <c>https://acme.app.example.com</c>.</summary>
    public string? FrontendRootDomain { get; set; }

    /// <summary>Allow the tenants' verified custom domains as origins (<c>https://{host}</c>).</summary>
    public bool AllowVerifiedCustomDomains { get; set; } = true;
}

/// <summary>Platform support impersonation (<c>MultiTenancy:Impersonation</c>).</summary>
public sealed class TenantImpersonationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Lifetime of an impersonation access token (no refresh token is issued). 5-60 minutes.</summary>
    public int MaxMinutes { get; set; } = 30;
}

/// <summary>How the current tenant is found for a request.</summary>
public sealed class TenantResolutionOptions
{
    /// <summary>JWT claim holding the active tenant id. The primary, unforgeable source.</summary>
    public string ClaimType { get; set; } = TenantClaimTypes.TenantId;

    /// <summary>Header carrying a tenant id or identifier (anonymous pre-login calls, SPAs, service calls).</summary>
    public string HeaderName { get; set; } = "X-Tenant";

    /// <summary>Optional header that selects the active branch (must be inside the user's branch scope).</summary>
    public string BranchHeaderName { get; set; } = "X-Branch";

    public bool EnableHeader { get; set; } = true;

    public bool EnableHost { get; set; } = true;

    /// <summary>
    /// Root domain for subdomain resolution, e.g. <c>api.example.com</c> → <c>acme.api.example.com</c> is tenant
    /// <c>acme</c>. Empty = subdomain resolution off (verified custom domains still work).
    /// </summary>
    public string? RootDomain { get; set; }

    /// <summary>Subdomains that are never tenant identifiers.</summary>
    public string[] ReservedSubdomains { get; set; } = ["www", "api", "app", "admin", "host", "status", "mail"];
}

/// <summary>How host scope bypasses Row-Level Security.</summary>
public enum RlsBypassMode
{
    /// <summary>Session variable <c>app.bypass_rls = on</c> on the runtime connection (works with a single DB role).</summary>
    SessionVariable = 0,

    /// <summary>A separate <c>BYPASSRLS</c> database role for host scope; the runtime role cannot bypass.</summary>
    Role = 1
}

/// <summary>Database-level isolation settings.</summary>
public sealed class TenantDatabaseOptions
{
    /// <summary>
    /// Postgres Row-Level Security as a second isolation layer under the EF filters. When <c>false</c>, the
    /// session runs with <c>app.bypass_rls = on</c> (policies stay installed but are inert).
    /// RLS does not apply to superusers - run the app with a dedicated non-superuser role in production.
    /// </summary>
    public bool EnableRowLevelSecurity { get; set; } = true;

    /// <summary>
    /// How host-scope work bypasses RLS. <see cref="RlsBypassMode.SessionVariable"/> (default) sets
    /// <c>app.bypass_rls</c> on the runtime connection. <see cref="RlsBypassMode.Role"/> connects host scope with the
    /// credentials of <see cref="HostConnectionString"/> (a <c>BYPASSRLS</c> role) and never sets the variable on the
    /// runtime role, which with the <c>tenant_rls_bypass</c> group role installed cannot switch RLS off itself.
    /// </summary>
    public RlsBypassMode RlsBypass { get; set; } = RlsBypassMode.SessionVariable;

    /// <summary>
    /// Role mode only: connection string of the host/bypass role. Its username and password replace the runtime
    /// credentials on every host-scope connection (server and database stay those of the connection being opened,
    /// so dedicated databases on the same server work; the role must exist there too).
    /// </summary>
    public string? HostConnectionString { get; set; }

    /// <summary>
    /// Configuration section that holds dedicated-tenant connection strings by
    /// <see cref="TenantInfo.ConnectionSecretRef"/> (e.g. <c>MultiTenancy:Connections:acme</c>, or a Docker secret
    /// mapped to that key). <c>ConnectionStrings:{ref}</c> is checked as well.
    /// </summary>
    public string ConnectionsSection { get; set; } = "MultiTenancy:Connections";

    /// <summary>
    /// Database name of a dedicated tenant that has NO <see cref="TenantInfo.ConnectionSecretRef"/>: the shared
    /// connection string is reused (same server and credentials) with this database name.
    /// Tokens: <c>{database}</c> (shared database name), <c>{identifier}</c> (tenant slug).
    /// The app role needs <c>CREATEDB</c> to provision such databases.
    /// </summary>
    public string DedicatedDatabaseNameFormat { get; set; } = "{database}_t_{identifier}";

    /// <summary>Npgsql <c>Maximum Pool Size</c> applied to dedicated-tenant connections (bounds total connections).</summary>
    public int MaxDedicatedPoolSize { get; set; } = 20;

    /// <summary>Parallelism of the dedicated-database migrator.</summary>
    public int MigrationParallelism { get; set; } = 4;
}

public sealed class TenantCacheOptions
{
    /// <summary>In-process (L1) cache lifetime of tenant/membership snapshots.</summary>
    public int LocalSeconds { get; set; } = 30;

    /// <summary>Distributed (L2, Redis) cache lifetime of tenant/membership snapshots.</summary>
    public int DistributedSeconds { get; set; } = 600;

    /// <summary>
    /// Broadcast L1 invalidations to the other instances over Redis pub/sub (no effect without Redis). Off = other
    /// instances wait for <see cref="LocalSeconds"/>.
    /// </summary>
    public bool BroadcastInvalidation { get; set; } = true;

    /// <summary>Redis pub/sub channel of the L1 invalidation broadcast.</summary>
    public string InvalidationChannel { get; set; } = "starterkit:tenancy:invalidate";
}

/// <summary>Claim types used by multi-tenancy.</summary>
public static class TenantClaimTypes
{
    /// <summary>Active tenant id.</summary>
    public const string TenantId = "tid";

    /// <summary>Membership stamp at issue time; a changed membership makes the token stale.</summary>
    public const string MembershipStamp = "tms";

    /// <summary>Impersonating platform user (RFC 8693 <c>act</c> semantics, flattened to the actor's id).</summary>
    public const string Actor = "act";
}
