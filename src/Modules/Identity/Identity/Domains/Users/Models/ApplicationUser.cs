using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Users.Models;

/// <summary>
/// Application user extending ASP.NET Core Identity
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    // Additional profile fields
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }

    /// <summary>Tenant of the user's last tenant session; preferred at the next login (multi-tenancy).</summary>
    public Guid? LastActiveTenantId { get; set; }
    
    // OAuth provider information
    public string? OAuthProvider { get; set; }  // Google, Facebook, None
    public string? OAuthProviderId { get; set; }
    
    // Phone verification (Identity handles phone, but we track verification).
    // OTP codes are NOT stored on the user: see Identity.Services.Otp (HMAC-hashed, attempt-limited).
    public bool IsPhoneNumberVerified { get; set; }
    
    /// <summary>
    /// Domain suffix for placeholder emails assigned to phone-only registrations.
    /// </summary>
    public const string PhonePlaceholderEmailDomain = "@phone.starterkit.local";

    // Profile completion tracking
    public bool IsProfileComplete { get; set; }
    public DateTime? ProfileCompletedAt { get; set; }
    
    // Account status
    public bool IsActive { get; set; } = true;
    public bool MustChangePasswordOnNextLogin { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Full name helper
    public string FullName => $"{FirstName} {LastName}".Trim();
    
    // Check if profile is complete (only phone verification is mandatory)
    public void UpdateProfileCompletion()
    {
        var wasComplete = IsProfileComplete;
        
        IsProfileComplete = IsPhoneNumberVerified;
        
        if (IsProfileComplete && !wasComplete)
        {
            ProfileCompletedAt = DateTime.UtcNow;
        }
        
        UpdatedAt = DateTime.UtcNow;
    }
}
