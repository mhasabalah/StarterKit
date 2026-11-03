namespace Identity.Models;

/// <summary>
/// Refresh token row. Only the SHA-256 hash of the token is stored.
/// Tokens are rotated on every refresh; all tokens descending from one login share a <see cref="FamilyId"/>,
/// so re-use of an already rotated token can revoke the whole family (stolen-token detection).
/// </summary>
public class RefreshToken
{
    public const string RotatedReason = "Rotated";

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 of the raw token. Never store raw tokens.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens issued from one login (and their rotations) share this id.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>Set when this token was rotated: the id of the token that replaced it.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>Hash of the user's security stamp when the token was issued. A stamp change invalidates the token.</summary>
    public string SecurityStampHash { get; set; } = string.Empty;

    /// <summary>
    /// The login that started this family passed a second factor (2FA). Copied on rotation so refreshed access
    /// tokens keep their <c>amr=mfa</c> claim.
    /// </summary>
    public bool MultiFactor { get; set; }

    /// <summary>
    /// Active tenant of the session (multi-tenancy). Copied on rotation; a refresh re-validates the membership and
    /// drops the tenant (tenant-less session) when the user is no longer a member.
    /// </summary>
    public Guid? TenantId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string CreatedByIp { get; set; } = string.Empty;

    public DateTime? RevokedAt { get; set; }

    public string? RevokedByIp { get; set; }

    public string? RevokedReason { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsActive => !IsRevoked && !IsExpired;

    public ApplicationUser User { get; set; } = null!;
}
