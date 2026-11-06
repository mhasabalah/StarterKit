using System.Buffers.Text;
using System.Security.Cryptography;
using Shared.Core.Auditing;

namespace Identity.Domains.Scim.Models;

/// <summary>
/// A tenant's SCIM endpoint. The IdP authenticates with a bearer token <c>scim_{prefix}_{secret}</c>: the prefix
/// locates the row, only a SHA-256 of the whole token is stored, comparison is constant-time.
/// </summary>
public class ScimDirectory : Aggregate<Guid>, IAuditable
{
    public const string TokenScheme = "scim_";
    public const int NameMaxLength = 100;
    private const int PrefixLength = 12;

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool Enabled { get; private set; }
    public string TokenPrefix { get; private set; } = default!;

    [AuditIgnore]
    public string TokenHash { get; private set; } = default!;

    [AuditIgnore]
    public DateTime? LastRequestAt { get; private set; }

    private ScimDirectory()
    {
    }

    /// <returns>The directory and its plain token (show once).</returns>
    public static (ScimDirectory Directory, string Token) Create(Guid tenantId, string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
            throw new BadRequestException($"Name is required (max {NameMaxLength} characters).");

        var directory = new ScimDirectory { Id = Guid.CreateVersion7(), TenantId = tenantId, Name = trimmed, Enabled = true };
        var token = directory.RotateToken();
        return (directory, token);
    }

    public string RotateToken()
    {
        TokenPrefix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PrefixLength / 2));
        var token = $"{TokenScheme}{TokenPrefix}_{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32))}";
        TokenHash = Hash(token);
        return token;
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    public void Touch(DateTime now) => LastRequestAt = now;

    public bool Matches(string token) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(TokenHash));

    /// <summary>The lookup prefix of a presented token, or null when it is not one of ours.</summary>
    public static string? PrefixOf(string? token)
    {
        if (token is null || !token.StartsWith(TokenScheme, StringComparison.Ordinal) || token.Length > 200)
            return null;
        var parts = token[TokenScheme.Length..].Split('_', 2);
        return parts.Length == 2 && parts[0].Length == PrefixLength ? parts[0] : null;
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// The SCIM enterprise user extension (RFC 7643 §4.3, <c>urn:ietf:params:scim:schemas:extension:enterprise:2.0:User</c>).
/// <see cref="ManagerId"/> is the SCIM id (= account id) of a user of the SAME directory.
/// </summary>
public sealed record ScimEnterprise(
    string? EmployeeNumber = null,
    string? CostCenter = null,
    string? Organization = null,
    string? Division = null,
    string? Department = null,
    Guid? ManagerId = null)
{
    public const int MaxLength = 256;

    public static ScimEnterprise Empty { get; } = new();

    public bool IsEmpty => this == Empty;
}

/// <summary>
/// The directory's view of one user (the SCIM resource). <see cref="UserId"/> is the account it provisions and
/// also the SCIM <c>id</c>. Rows are per directory: the same person can be provisioned by two tenants.
/// </summary>
public class ScimUser
{
    public Guid DirectoryId { get; private set; }
    public Guid UserId { get; private set; }
    public string UserName { get; private set; } = default!;
    public string? ExternalId { get; private set; }
    public bool Active { get; private set; }
    public string? EmployeeNumber { get; private set; }
    public string? CostCenter { get; private set; }
    public string? Organization { get; private set; }
    public string? Division { get; private set; }
    public string? Department { get; private set; }

    /// <summary>The manager's SCIM id (account id) within the same directory.</summary>
    public Guid? ManagerScimUserId { get; private set; }

    public DateTime Created { get; private set; }
    public DateTime LastModified { get; private set; }
    public long Revision { get; private set; }

    /// <summary>The enterprise extension as a value.</summary>
    public ScimEnterprise Enterprise =>
        new(EmployeeNumber, CostCenter, Organization, Division, Department, ManagerScimUserId);

    private ScimUser()
    {
    }

    public static ScimUser Create(Guid directoryId, Guid userId, string userName, string? externalId, bool active,
        ScimEnterprise? enterprise, DateTime now)
    {
        var user = new ScimUser
        {
            DirectoryId = directoryId, UserId = userId, UserName = userName, ExternalId = externalId, Active = active,
            Created = now, LastModified = now, Revision = 1
        };
        user.SetEnterprise(enterprise);
        return user;
    }

    public void Update(string userName, string? externalId, bool active, ScimEnterprise enterprise, DateTime now)
    {
        UserName = userName;
        ExternalId = externalId;
        Active = active;
        SetEnterprise(enterprise);
        LastModified = now;
        Revision++;
    }

    private void SetEnterprise(ScimEnterprise? enterprise)
    {
        enterprise ??= ScimEnterprise.Empty;
        EmployeeNumber = Cap(enterprise.EmployeeNumber);
        CostCenter = Cap(enterprise.CostCenter);
        Organization = Cap(enterprise.Organization);
        Division = Cap(enterprise.Division);
        Department = Cap(enterprise.Department);
        ManagerScimUserId = enterprise.ManagerId == UserId ? null : enterprise.ManagerId; // nobody manages themselves
    }

    private static string? Cap(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Trim() is { Length: > ScimEnterprise.MaxLength } t ? t[..ScimEnterprise.MaxLength] : value.Trim();
}

/// <summary>A directory group; optionally mapped to a tenant role. Members are account ids.</summary>
public class ScimGroup
{
    public Guid Id { get; private set; }
    public Guid DirectoryId { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public string? ExternalId { get; private set; }
    public Guid? TenantRoleId { get; private set; }
    public List<Guid> Members { get; private set; } = [];
    public DateTime Created { get; private set; }
    public DateTime LastModified { get; private set; }
    public long Revision { get; private set; }

    private ScimGroup()
    {
    }

    public static ScimGroup Create(Guid directoryId, string displayName, string? externalId, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(), DirectoryId = directoryId, DisplayName = displayName, ExternalId = externalId,
        Created = now, LastModified = now, Revision = 1
    };

    public void Rename(string displayName, string? externalId, DateTime now)
    {
        DisplayName = displayName;
        ExternalId = externalId;
        Changed(now);
    }

    public void MapRole(Guid? tenantRoleId, DateTime now)
    {
        TenantRoleId = tenantRoleId;
        Changed(now);
    }

    public void SetMembers(IEnumerable<Guid> members, DateTime now)
    {
        Members = members.Distinct().ToList();
        Changed(now);
    }

    public void AddMembers(IEnumerable<Guid> members, DateTime now) => SetMembers(Members.Concat(members), now);

    public void RemoveMembers(IEnumerable<Guid> members, DateTime now)
    {
        var removed = members.ToHashSet();
        SetMembers(Members.Where(m => !removed.Contains(m)), now);
    }

    private void Changed(DateTime now)
    {
        LastModified = now;
        Revision++;
    }
}
