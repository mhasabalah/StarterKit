namespace Tenancy.Domains.Branches.Models;

/// <summary>
/// A branch of a tenant's organization tree (HQ → region → branch …). Tenant-owned.
/// <para>
/// <b>Materialized path.</b> <see cref="Path"/> is <c>/{rootId}/…/{id}/</c> (ids as 32-char hex), so "this branch and
/// its descendants" is a single indexed prefix query (<c>path LIKE '/root/x/%'</c>) at any depth - no recursive CTE
/// per request. Moving a branch rewrites the prefix of its whole subtree in one set-based statement
/// (MoveBranchHandler).
/// </para>
/// Branches are deactivated, never deleted (business rows keep pointing at them).
/// </summary>
public class Branch : Aggregate<Guid>, IMustHaveTenant
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int PathMaxLength = 33 * 32 + 1; // supports MaxBranchDepth up to 32

    public Guid TenantId { get; set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Path { get; private set; } = default!;
    public int Depth { get; private set; }
    public bool IsHeadOffice { get; private set; }
    public bool IsActive { get; private set; }
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? TimeZone { get; private set; }

    private Branch()
    {
    }

    /// <param name="parent">Parent branch, or <c>null</c> for a root (the head office is a root).</param>
    /// <param name="maxDepth"><c>MultiTenancy:MaxBranchDepth</c>.</param>
    public static Branch Create(string code, string name, Branch? parent, int maxDepth, bool isHeadOffice = false,
        string? address = null, string? phone = null, string? timeZone = null, Guid? id = null)
    {
        var branchId = id ?? Guid.CreateVersion7();
        var depth = (parent?.Depth ?? 0) + 1;
        if (depth > maxDepth)
            throw new InvalidBranchException($"The branch tree can be at most {maxDepth} levels deep.");
        if (parent is { IsActive: false })
            throw new InvalidBranchException("Cannot add a branch under an inactive branch.");

        var branch = new Branch
        {
            Id = branchId,
            ParentId = parent?.Id,
            Code = NormalizeCode(code),
            Name = RequireName(name),
            Depth = depth,
            Path = (parent?.Path ?? "/") + Segment(branchId),
            IsHeadOffice = isHeadOffice,
            IsActive = true
        };

        if (parent is not null) branch.TenantId = parent.TenantId;
        branch.UpdateDetails(name, address, phone, timeZone);
        return branch;
    }

    public void UpdateDetails(string name, string? address, string? phone, string? timeZone)
    {
        Name = RequireName(name);
        Address = Trim(address, AddressMaxLength);
        Phone = Trim(phone, 32);
        TimeZone = Trim(timeZone, 64);
    }

    public void Deactivate()
    {
        if (IsHeadOffice) throw new InvalidBranchException("The head office cannot be deactivated.");
        IsActive = false;
    }

    public void Activate() => IsActive = true;

    /// <summary>
    /// Re-parents this branch. Returns the old and new path prefixes; the caller rewrites the descendants' paths
    /// with them (set-based) in the same transaction.
    /// </summary>
    public (string OldPrefix, string NewPrefix, int DepthDelta) MoveUnder(Branch? newParent, int maxDepth,
        int subtreeHeight)
    {
        if (newParent is not null)
        {
            if (newParent.Id == Id || newParent.Path.StartsWith(Path, StringComparison.Ordinal))
                throw new InvalidBranchException("A branch cannot be moved under itself or one of its descendants.");
            if (!newParent.IsActive)
                throw new InvalidBranchException("Cannot move a branch under an inactive branch.");
        }

        var newDepth = (newParent?.Depth ?? 0) + 1;
        if (newDepth + subtreeHeight > maxDepth)
            throw new InvalidBranchException($"The branch tree can be at most {maxDepth} levels deep.");

        var oldPrefix = Path;
        var newPrefix = (newParent?.Path ?? "/") + Segment(Id);
        var delta = newDepth - Depth;

        ParentId = newParent?.Id;
        Path = newPrefix;
        Depth = newDepth;
        return (oldPrefix, newPrefix, delta);
    }

    public bool IsAncestorOrSelfOf(string path) => path.StartsWith(Path, StringComparison.Ordinal);

    public static string Segment(Guid id) => $"{id:N}/";

    public static string NormalizeCode(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > CodeMaxLength ||
            !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new InvalidBranchException($"Branch code is required (max {CodeMaxLength}: A-Z, 0-9, '-', '_').");
        }

        return normalized;
    }

    private static string RequireName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new InvalidBranchException($"Branch name is required (max {NameMaxLength} characters).");
        return trimmed;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
