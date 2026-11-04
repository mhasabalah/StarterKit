# Identity Module

## Overview

The **Identity** module handles authentication, authorization, user management, and security for the StarterKit platform.

> Admin governance (roles → permissions, `perm:*` policies, admin TOTP 2FA, the admin audit log):
> see [Permissions-2FA-and-Admin-Audit.md](Permissions-2FA-and-Admin-Audit.md).

## Responsibilities

- User authentication (login, logout, token management)
- User registration and profile management
- Role and permission management
- Password management (reset, change)
- JWT token generation and validation
- Claims-based authorization
- Multi-factor authentication (MFA)
- Session management

## Technology Stack

- **ASP.NET Core Identity**: User management framework
- **JWT Bearer**: Token-based authentication
- **BCrypt/PBKDF2**: Password hashing
- **Redis**: Session and token storage
- **Mediator** (`Shared.Contracts.Mediation`, owned): command/query handling

## Domain Model

### Key Entities

#### User
```csharp
public class ApplicationUser : IdentityUser<Guid>
{
    public string ArabicName { get; set; }
    public string EnglishName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public Guid? ProfileId { get; set; }
    public Profile? Profile { get; set; }
}
```

#### Role
```csharp
public class ApplicationRole : IdentityRole<Guid>
{
    public string ArabicName { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
}
```

#### RefreshToken
```csharp
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
```

## Features

### Endpoints

The full, code-generated route table (auth, onboarding, profile, users, admins, impersonation, audit), with the
authorization and rate limit of each route, is in **[Users-and-Admins-API.md](Users-and-Admins-API.md)**. In short:

- **Auth:** `api/auth/{register,login,refresh,logout,change-password,forgot-password,verify-reset-otp,reset-password}`,
  `api/auth/email/*`, `api/auth/2fa/*`, and `api/auth/switch-tenant` (multi-tenancy).
- **Users and admins (platform staff, permission-gated):** `api/identity/users/*`, `api/identity/admins/*`.
- **Roles:** there is no role CRUD API. Roles are seeded (`Domains/Roles/Constants/RoleConstants.cs`), assigned with
  `POST api/identity/users/{id}/roles` and revoked with `DELETE api/identity/users/{id}/roles/{roleName}`.
  Permissions per role are code (`RolePermissionMap`).
- **Token flow details:** [jwt/JWT-AUTH-TECHNICAL-GUIDE.md](jwt/JWT-AUTH-TECHNICAL-GUIDE.md) and
  [jwt/JWT-AUTH-FRONTEND-GUIDE.md](jwt/JWT-AUTH-FRONTEND-GUIDE.md).

### One-time codes (OTP)

Identity owns the application's ONE one-time-code system: `IOtpCodeService` (`Services/Otp`), stored in
`identity.otp_codes` (one row per user and `OtpPurpose`). Notification only delivers codes.

| Purpose | Used by | Delivery |
|---|---|---|
| `PhoneVerification` | `POST /api/identity/phone/send-verification` + verify; `/notifications/verification/*` (PhoneVerification) | SMS (`ISmsService`) / WhatsApp |
| `PasswordReset` | `forgot-password` (phone) + `verify-reset-otp` | SMS (`ISmsService`) |
| `OnboardingPhoneVerification` | onboarding register / resend-otp / verify-phone, `OnboardingOtpRetryJob` | WhatsApp (`IOtpSender` → Notification `IOtpDeliveryService`) |
| `EmailVerification`, `TwoFactorLogin` | `/notifications/verification/*` | Notification (email has no OTP template yet) |

Guarantees:
- Codes from a CSPRNG; stored only as HMAC-SHA256(`Identity:OtpHmacKey` / secret `identity_otp_hmac_key`,
  `purpose:user:code`), so a code never verifies another user or purpose. Constant-time compare.
- **Issue** is one conditional `UPDATE` (or the first `INSERT`): the resend cooldown
  (`PhoneVerification:ResendCooldownSeconds`) and the send cap (`PhoneVerification:MaxSendsPerDay`, rolling 24h) are
  checked atomically, so parallel requests cannot both pass. A new code replaces (invalidates) the previous one and
  resets its attempts. Refusals throw `OtpRateLimitedException` (429, `Reason` = `Cooldown` / `SendCapReached`).
- **Verify** reserves an attempt atomically BEFORE comparing (`attempts < max_attempts`), so parallel guesses never
  exceed `MaxVerificationAttempts` (`PasswordReset:MaxOtpAttempts` for resets); a correct code is consumed by a
  compare-and-set, so parallel correct submissions succeed exactly once. The row is kept (cooldown + cap survive a
  successful verify).
- `ReleaseAsync` (delivery failed: invalidate that code, lift the cooldown, keep the cap) and `InvalidateAsync`
  (onboarding timeout). `IOtpSender` = issue + Notification delivery + release on failure, as a result (no throw).

### Email Confirmation

Email accounts start with `EmailConfirmed = false`. Forgot-password by email needs a confirmed email, so confirmation is
part of onboarding.

| Step | Endpoint | Auth |
|------|----------|------|
| Link sent automatically | on `POST /api/auth/register` (email) and when `complete-profile` adds an email to a phone account | – |
| Resend | `POST /api/auth/email/send-confirmation` (no body; always the caller) | signed in, `otp` rate limit |
| Confirm | `POST /api/auth/email/confirm` `{ "userId": "...", "token": "..." }` | anonymous, `auth` rate limit |

**Link and frontend:**
- The email links to `{EmailConfirmation:FrontendBaseUrl}/confirm-email?userId=..&token=..`.
- The frontend page posts both values to `/api/auth/email/confirm`.

**Tokens:**
- Tokens come from their own provider (`EmailConfirmationTokenProvider`), with their own Data Protection purpose and lifetime: `EmailConfirmation:TokenExpiryHours` (default 24h). Password-reset tokens stay short.
- Tokens are bound to the security stamp, so changing the email or password invalidates older links.
- Unknown user, wrong token and expired token all return the same 400.

**Other behaviour:**
- The automatic send is best effort; registration never fails because mail is down. Turn it off with `EmailConfirmation:SendOnRegister=false`.
- The `email_verified` claim in existing access tokens updates at the next refresh.
- Phone accounts carry a non-routable placeholder email. They get no confirmation mail until they add a real email.

#### Reset Password
- **Endpoint**: `POST api/auth/reset-password`
- **Request**:
```json
{
  "email": "user@example.com",
  "token": "reset-token-here",
  "newPassword": "NewPassword123!"
}
```

## Security Configuration

### JWT Configuration
```csharp
public class JwtSettings
{
    public string SecretKey { get; set; }
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int AccessTokenExpirationMinutes { get; set; } = 60;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
```

### Password Policy
```csharp
services.Configure<IdentityOptions>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    
    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    
    // User settings
    options.User.RequireUniqueEmail = true;
});
```

### Claims
```csharp
var claims = new List<Claim>
{
    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new(ClaimTypes.Name, user.UserName),
    new(ClaimTypes.Email, user.Email),
    new("ArabicName", user.ArabicName),
    new("EnglishName", user.EnglishName)
};

// Add role claims
foreach (var role in userRoles)
{
    claims.Add(new Claim(ClaimTypes.Role, role));
}
```

## Authorization Policies

### Predefined Policies
```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministratorRole", 
        policy => policy.RequireRole("Admin"));
    
    options.AddPolicy("RequireAgentRole", 
        policy => policy.RequireRole("Agent", "Admin"));
    
    options.AddPolicy("RequireActiveUser", 
        policy => policy.RequireClaim("IsActive", "true"));
    
    options.AddPolicy("UserManagement", 
        policy => policy.RequireRole("Admin", "UserManager"));
});
```

## Database Schema

### Tables
- `identity.users` - User accounts
- `identity.roles` - Application roles
- `identity.user_roles` - User-role assignments
- `identity.user_claims` - User claims
- `identity.user_logins` - External login providers
- `identity.user_tokens` - User tokens
- `identity.refresh_tokens` - Refresh tokens

### Migrations
```bash
cd src\Modules\Identity\Identity
dotnet ef migrations add InitialIdentity --context IdentityDbContext
dotnet ef database update --context IdentityDbContext
```

## Integration with Other Modules

### Domain Events
```csharp
// Published events
public record UserRegisteredEvent(Guid UserId, string Email) : IDomainEvent;
public record UserLoggedInEvent(Guid UserId, DateTime LoginAt) : IDomainEvent;
public record PasswordChangedEvent(Guid UserId) : IDomainEvent;

// Consumed events
// None - Identity is foundational
```

## Best Practices

### ✅ Do's
- Always use secure password hashing
- Implement token refresh mechanism
- Log all authentication attempts
- Use HTTPS for all endpoints
- Implement rate limiting for login
- Store tokens securely (HttpOnly cookies)
- Validate JWT signatures
- Use strong secret keys (256-bit minimum)

### ❌ Don'ts
- Don't store passwords in plain text
- Don't log sensitive data (passwords, tokens)
- Don't expose user enumeration vulnerabilities
- Don't use weak JWT secret keys
- Don't skip authorization checks
- Don't trust client-side validation only

## Testing

### Unit Tests
```csharp
[Fact]
public async Task Login_WithValidCredentials_ReturnsToken()
{
    // Arrange
    var command = new LoginCommand("user@example.com", "password");
    
    // Act
    var result = await _handler.Handle(command, CancellationToken.None);
    
    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.AccessToken.Should().NotBeNullOrEmpty();
}
```

### Integration Tests
```csharp
[Fact]
public async Task POST_Login_Returns_Token()
{
    // Arrange
    var request = new LoginRequest("user@example.com", "password");
    
    // Act
    var response = await _client.PostAsJsonAsync("/api/auth/login", request);
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var content = await response.Content.ReadFromJsonAsync<LoginResponse>();
    content.AccessToken.Should().NotBeNullOrEmpty();
}
```

## Common Issues & Solutions

### Issue: Token expiration
**Solution**: Implement refresh token mechanism

### Issue: User enumeration
**Solution**: Return generic error messages for login failures

### Issue: Weak passwords
**Solution**: Enforce strong password policy

## Next Steps
- [Users-and-Admins-API.md](Users-and-Admins-API.md): every Identity route with its authorization
- [Permissions-2FA-and-Admin-Audit.md](Permissions-2FA-and-Admin-Audit.md): permissions, 2FA, audit, impersonation
- [Replacing-the-Identity-Provider.md](Replacing-the-Identity-Provider.md): the `UserDirectory.Contracts` port
- [Catalog module](../Modules/Catalog.md): the reference module for new work
