using System.Security.Cryptography;
using System.Text;
using Tenancy.Domains.Members.Models;

namespace Tenancy.Domains.Invitations.Models;

/// <summary>
/// An invitation to join a tenant, addressed to an e-mail. Only the SHA-256 of the token is stored; the raw token is
/// e-mailed (<see cref="Services.TenantInvitationMailer"/>). Accepting requires a signed-in user whose (confirmed) e-mail matches.
/// </summary>
public class TenantInvitation : Aggregate<Guid>, IMustHaveTenant
{
    public const int EmailMaxLength = 256;

    public Guid TenantId { get; set; }
    public string Email { get; private set; } = default!;
    public List<Guid> RoleIds { get; private set; } = [];
    public List<BranchAssignment> Branches { get; private set; } = [];
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public TenantInvitationStatus Status { get; private set; }
    public Guid InvitedBy { get; private set; }
    public Guid? AcceptedBy { get; private set; }
    public DateTime? AcceptedAt { get; private set; }

    private TenantInvitation()
    {
    }

    /// <returns>The invitation and the raw token (never stored).</returns>
    public static (TenantInvitation Invitation, string Token) Create(Guid tenantId, string email,
        IEnumerable<Guid> roleIds, IEnumerable<BranchAssignment> branches, Guid invitedBy, DateTime utcNow,
        TimeSpan lifetime)
    {
        var normalized = NormalizeEmail(email);
        var token = NewToken();

        var invitation = new TenantInvitation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Email = normalized,
            RoleIds = roleIds.Distinct().ToList(),
            Branches = branches.ToList(),
            TokenHash = HashToken(token),
            ExpiresAt = utcNow.Add(lifetime),
            Status = TenantInvitationStatus.Pending,
            InvitedBy = invitedBy
        };

        return (invitation, token);
    }

    /// <summary>
    /// Re-sends a pending (possibly expired) invitation: a NEW token replaces the old one (the previous link stops
    /// working) and the lifetime restarts.
    /// </summary>
    /// <returns>The new raw token (never stored).</returns>
    public string Renew(DateTime utcNow, TimeSpan lifetime)
    {
        if (Status is not (TenantInvitationStatus.Pending or TenantInvitationStatus.Expired))
            throw new InvalidMembershipException("Only pending or expired invitations can be re-sent.");

        var token = NewToken();
        TokenHash = HashToken(token);
        ExpiresAt = utcNow.Add(lifetime);
        Status = TenantInvitationStatus.Pending;
        return token;
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public bool IsUsable(DateTime utcNow) => Status == TenantInvitationStatus.Pending && ExpiresAt > utcNow;

    public void Accept(Guid userId, string? userEmail, DateTime utcNow)
    {
        if (Status != TenantInvitationStatus.Pending)
            throw new InvalidMembershipException("This invitation is no longer valid.");
        if (ExpiresAt <= utcNow)
        {
            Status = TenantInvitationStatus.Expired;
            throw new InvalidMembershipException("This invitation has expired.");
        }

        if (!string.Equals(NormalizeEmail(userEmail ?? string.Empty), Email, StringComparison.Ordinal))
            throw new InvalidMembershipException("This invitation was sent to a different e-mail address.");

        Status = TenantInvitationStatus.Accepted;
        AcceptedBy = userId;
        AcceptedAt = utcNow;
    }

    public void Revoke()
    {
        if (Status != TenantInvitationStatus.Pending)
            throw new InvalidMembershipException("Only pending invitations can be revoked.");
        Status = TenantInvitationStatus.Revoked;
    }

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    public static string NormalizeEmail(string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > EmailMaxLength || !normalized.Contains('@'))
            throw new InvalidMembershipException("A valid e-mail address is required.");
        return normalized;
    }
}
