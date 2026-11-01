# Identity Module

## Overview

The **Identity** module handles authentication, authorization, user management, and security for the StarterKit platform.

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
- **MediatR**: Command/query handling

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

### Authentication

#### Login
- **Endpoint**: `POST /api/identity/auth/login`
- **Request**:
```json
{
  "userName": "user@example.com",
  "password": "SecurePassword123!",
  "rememberMe": true
}
```
- **Response**:
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "abc123...",
  "expiresIn": 3600,
  "user": {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "userName": "user@example.com",
    "email": "user@example.com",
    "roles": ["Agent"]
  }
}
```

#### Register
- **Endpoint**: `POST /api/identity/auth/register`
- **Request**:
```json
{
  "userName": "newuser",
  "email": "newuser@example.com",
  "password": "SecurePassword123!",
  "arabicName": "مستخدم جديد",
  "englishName": "New User",
  "phoneNumber": "+966501234567"
}
```

#### Refresh Token
- **Endpoint**: `POST /api/identity/auth/refresh`
- **Request**:
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "abc123..."
}
```

#### Logout
- **Endpoint**: `POST /api/identity/auth/logout`
- **Authorization**: Required

### User Management

#### Get Users
- **Endpoint**: `GET /api/identity/users?pageNumber=1&pageSize=10&searchTerm=john`
- **Authorization**: Admin only

#### Get User by ID
- **Endpoint**: `GET /api/identity/users/{id}`
- **Authorization**: Admin or self

#### Update User
- **Endpoint**: `PUT /api/identity/users/{id}`
- **Request**:
```json
{
  "arabicName": "أحمد محمد",
  "englishName": "Ahmed Mohammed",
  "email": "ahmed@example.com",
  "phoneNumber": "+966501234567"
}
```

#### Delete User (Soft Delete)
- **Endpoint**: `DELETE /api/identity/users/{id}`
- **Authorization**: Admin only

### Role Management

#### Get Roles
- **Endpoint**: `GET /api/identity/roles`
- **Authorization**: Admin only

#### Create Role
- **Endpoint**: `POST /api/identity/roles`
- **Request**:
```json
{
  "name": "PropertyManager",
  "arabicName": "مدير العقارات",
  "description": "Manages property listings"
}
```

#### Assign Role to User
- **Endpoint**: `POST /api/identity/users/{userId}/roles`
- **Request**:
```json
{
  "roleId": "123e4567-e89b-12d3-a456-426614174000"
}
```

### Password Management

#### Change Password
- **Endpoint**: `POST /api/identity/auth/change-password`
- **Request**:
```json
{
  "currentPassword": "OldPassword123!",
  "newPassword": "NewPassword123!"
}
```

#### Forgot Password
- **Endpoint**: `POST /api/identity/auth/forgot-password`
- **Request**:
```json
{
  "email": "user@example.com"
}
```

#### Reset Password
- **Endpoint**: `POST /api/identity/auth/reset-password`
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
    var response = await _client.PostAsJsonAsync("/api/identity/auth/login", request);
    
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
- [Catalog Module](../Catalog/README.md)
- [Properties Module](../Properties/README.md)
- [Admin Management](AdminManagement.md)
