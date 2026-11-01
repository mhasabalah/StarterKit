namespace Identity.Models;

/// <summary>
/// Refresh token entity for secure token management
/// Implements token rotation and revocation tracking
/// </summary>
public class RefreshToken
{
    /// <summary>
    /// Unique identifier for this refresh token
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// User who owns this refresh token
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA256 hash of the refresh token
    /// Never store raw tokens in database
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// When this token expires (typically 7-30 days)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When this token was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// IP address where token was created (for audit trail)
    /// </summary>
    public string CreatedByIp { get; set; } = string.Empty;

    /// <summary>
    /// When this token was revoked (null if still active)
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// IP address that revoked the token
    /// </summary>
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// Reason for revocation (logout, replaced, security, etc.)
    /// </summary>
    public string? RevokedReason { get; set; }

    // Computed properties

    /// <summary>
    /// Check if token is expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Check if token is revoked
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>
    /// Check if token is still valid (not expired and not revoked)
    /// </summary>
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation properties

    /// <summary>
    /// User who owns this token
    /// </summary>
    public ApplicationUser User { get; set; } = null!;
}
