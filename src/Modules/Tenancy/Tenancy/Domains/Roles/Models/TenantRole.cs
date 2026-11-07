using Shared.Core.MultiTenancy.Authorization;

namespace Tenancy.Domains.Roles.Models;

/// <summary>
/// A role INSIDE a tenant: a named set of <see cref="TenantPermissions"/>. System roles (seeded per tenant, see
/// <see cref="SystemTenantRoles"/>) cannot be renamed or deleted; their permission sets come from code, so new
/// permissions reach existing tenants on deploy. Custom roles are fully tenant-defined.
/// </summary>
public class TenantRole : Aggregate<Guid>, IMustHaveTenant
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Key of a system role (<see cref="SystemTenantRoles"/>), <c>null</c> for custom roles.</summary>
    public string? SystemKey { get; private set; }

    /// <summary>Granted permissions (Postgres <c>text[]</c>). For system roles the code definition wins.</summary>
    public List<string> Permissions { get; private set; } = [];

    public bool IsSystem => SystemKey is not null;

    private TenantRole()
    {
    }

    public static TenantRole CreateCustom(Guid tenantId, string name, string? description, IEnumerable<string> permissions)
    {
        var role = new TenantRole { Id = Guid.CreateVersion7(), TenantId = tenantId };
        role.Update(name, description, permissions);
        return role;
    }

    public static TenantRole CreateSystem(Guid tenantId, SystemTenantRoles.Definition definition) => new()
    {
        Id = TenantSeedIds.For(tenantId, definition.SeedId),
        TenantId = tenantId,
        Name = definition.Name,
        Description = definition.Description,
        SystemKey = definition.Key,
        Permissions = [.. definition.Permissions]
    };

    /// <summary>Role names are joined with ';' in member import/export cells, so they cannot contain it.</summary>
    public static bool IsListSafeName(string? name) => name is null || !name.Contains(';', StringComparison.Ordinal);

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsSystem)
            throw new InvalidMembershipException("System roles cannot be changed.");

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new InvalidMembershipException($"Role name is required (max {NameMaxLength} characters).");
        if (!IsListSafeName(trimmed))
            throw new InvalidMembershipException("Role names must not contain ';'.");

        var granted = permissions.Distinct(StringComparer.Ordinal).ToList();
        var unknown = granted.Except(TenantPermissions.All, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            throw new InvalidMembershipException($"Unknown permission(s): {string.Join(", ", unknown)}.");

        Name = trimmed;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Permissions = granted;
        AddDomainEvent(new TenantRolesChangedEvent(TenantId));
    }

    /// <summary>Effective permissions: code definition for system roles (always current), stored set otherwise.</summary>
    public IEnumerable<string> EffectivePermissions =>
        SystemKey is not null && SystemTenantRoles.Find(SystemKey) is { } definition
            ? definition.Permissions
            : Permissions;
}

/// <summary>Roles every tenant gets. The owner flag (not a role) implies every permission.</summary>
public static class SystemTenantRoles
{
    public const string AdminKey = "admin";
    public const string BranchManagerKey = "branch-manager";
    public const string MemberKey = "member";

    public sealed record Definition(string Key, Guid SeedId, string Name, string Description, IReadOnlyList<string> Permissions);

    public static readonly Definition Admin = new(AdminKey, new Guid("60000000-0000-0000-0000-000000000001"),
        "Administrator", "Manages the whole tenant (everything except ownership).", TenantPermissions.All);

    public static readonly Definition BranchManager = new(BranchManagerKey, new Guid("60000000-0000-0000-0000-000000000002"),
        "Branch manager", "Runs the branches assigned to them.",
        [TenantPermissions.MembersRead, TenantPermissions.BranchesRead, TenantPermissions.ProductsManage]);

    public static readonly Definition Member = new(MemberKey, new Guid("60000000-0000-0000-0000-000000000003"),
        "Member", "Works inside the assigned branches.", [TenantPermissions.BranchesRead]);

    public static IReadOnlyList<Definition> All { get; } = [Admin, BranchManager, Member];

    public static Definition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    public static Guid IdOf(Guid tenantId, Definition definition) => TenantSeedIds.For(tenantId, definition.SeedId);
}
