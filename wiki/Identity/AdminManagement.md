# Admin Management - Identity Module

## Overview

Admin management provides advanced user and system administration capabilities within the Identity module.

## Features

### Admin Dashboard
- User statistics
- Active sessions monitoring
- Failed login attempts tracking
- Role distribution analytics

### Advanced User Management

#### Bulk Operations
- **Endpoint**: `POST /api/identity/admin/users/bulk-action`
- **Request**:
```json
{
  "userIds": [
    "123e4567-e89b-12d3-a456-426614174000",
    "987fcdeb-51a2-43f7-9012-345678901234"
  ],
  "action": "activate" // activate, deactivate, delete
}
```

#### User Impersonation
- **Endpoint**: `POST /api/identity/admin/users/{id}/impersonate`
- **Authorization**: SuperAdmin only
- **Response**:
```json
{
  "impersonationToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 3600,
  "originalUserId": "admin-user-id"
}
```

#### User Activity Log
- **Endpoint**: `GET /api/identity/admin/users/{id}/activity`
- **Response**:
```json
{
  "activities": [
    {
      "timestamp": "2024-01-10T10:30:00Z",
      "action": "Login",
      "ipAddress": "192.168.1.1",
      "userAgent": "Mozilla/5.0..."
    }
  ]
}
```

### Role & Permission Management

#### Create Custom Role
```csharp
public record CreateRoleCommand(
    string Name,
    string ArabicName,
    string? Description,
    List<string> Permissions
) : ICommand<Result<Guid>>;
```

#### Permission Structure
```json
{
  "module": "Properties",
  "permissions": [
    "properties.view",
    "properties.create",
    "properties.edit",
    "properties.delete",
    "properties.approve"
  ]
}
```

#### Assign Permissions to Role
- **Endpoint**: `POST /api/identity/admin/roles/{roleId}/permissions`
- **Request**:
```json
{
  "permissions": [
    "users.view",
    "users.edit",
    "properties.approve"
  ]
}
```

### Security Management

#### Force Password Reset
- **Endpoint**: `POST /api/identity/admin/users/{id}/force-password-reset`
- **Effect**: User must reset password on next login

#### Revoke All User Sessions
- **Endpoint**: `POST /api/identity/admin/users/{id}/revoke-sessions`
- **Effect**: All active tokens are invalidated

#### Two-Factor Authentication Management
- **Enable for user**: `POST /api/identity/admin/users/{id}/enable-2fa`
- **Disable for user**: `POST /api/identity/admin/users/{id}/disable-2fa`
- **Reset 2FA**: `POST /api/identity/admin/users/{id}/reset-2fa`

### Audit Logging

#### Get System Audit Logs
- **Endpoint**: `GET /api/identity/admin/audit-logs`
- **Query Parameters**:
  - `startDate`: Filter from date
  - `endDate`: Filter to date
  - `userId`: Filter by user
  - `action`: Filter by action type
  - `module`: Filter by module

- **Response**:
```json
{
  "logs": [
    {
      "id": "log-id",
      "timestamp": "2024-01-10T10:30:00Z",
      "userId": "user-id",
      "userName": "john.doe",
      "action": "UserCreated",
      "module": "Identity",
      "details": {
        "targetUserId": "new-user-id",
        "targetUserName": "jane.smith"
      },
      "ipAddress": "192.168.1.1"
    }
  ],
  "totalCount": 150
}
```

### System Configuration

#### Security Settings
```csharp
public class SecuritySettings
{
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public int PasswordExpirationDays { get; set; } = 90;
    public bool RequireTwoFactorForAdmins { get; set; } = true;
    public int SessionTimeoutMinutes { get; set; } = 60;
    public bool AllowConcurrentSessions { get; set; } = false;
}
```

#### Update Security Settings
- **Endpoint**: `PUT /api/identity/admin/settings/security`
- **Authorization**: SuperAdmin only

## Admin Roles

### Role Hierarchy
```
SuperAdmin (All permissions)
  ├── Admin (Module management)
  ├── UserManager (User management)
  ├── SecurityOfficer (Security & audit)
  └── SupportAgent (Read-only access)
```

### Predefined Roles

#### SuperAdmin
- Full system access
- Can create/delete admins
- System configuration
- Audit log access

#### Admin
- Module-level administration
- User management within scope
- Role assignment (non-admin roles)

#### UserManager
- User CRUD operations
- Role assignment (non-admin roles)
- User activity monitoring

#### SecurityOfficer
- Security settings management
- Audit log access
- Session monitoring
- 2FA management

## Implementation Examples

### Create Admin User
```csharp
public class CreateAdminHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager)
    : ICommandHandler<CreateAdminCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateAdminCommand command,
        CancellationToken cancellationToken)
    {
        // Create user
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.UserName,
            Email = command.Email,
            ArabicName = command.ArabicName,
            EnglishName = command.EnglishName,
            IsActive = true
        };
        
        var result = await userManager.CreateAsync(user, command.Password);
        if (!result.Succeeded)
            return Result.Failure<Guid>(IdentityErrors.UserCreationFailed);
        
        // Assign admin role
        await userManager.AddToRoleAsync(user, "Admin");
        
        // Add admin claims
        await userManager.AddClaimsAsync(user, new[]
        {
            new Claim("IsAdmin", "true"),
            new Claim("AdminLevel", "Module")
        });
        
        return Result.Success(user.Id);
    }
}
```

### Authorization Policy for Admin Actions
```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));
    
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireRole("SuperAdmin"));
    
    options.AddPolicy("UserManagement", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("SuperAdmin") ||
            context.User.IsInRole("Admin") ||
            context.User.IsInRole("UserManager")));
    
    options.AddPolicy("SecurityManagement", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("SuperAdmin") ||
            context.User.IsInRole("SecurityOfficer")));
});
```

### Audit Logging Behavior
```csharp
public class AuditLoggingBehavior<TRequest, TResponse>(
    ICurrentUserService currentUserService,
    IAuditLogRepository auditLogRepository)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var action = typeof(TRequest).Name;
        
        // Log before
        var startTime = DateTime.UtcNow;
        
        try
        {
            var response = await next();
            
            // Log success
            await auditLogRepository.LogAsync(new AuditLog
            {
                UserId = userId,
                Action = action,
                Timestamp = startTime,
                Success = true,
                Details = SerializeRequest(request)
            }, cancellationToken);
            
            return response;
        }
        catch (Exception ex)
        {
            // Log failure
            await auditLogRepository.LogAsync(new AuditLog
            {
                UserId = userId,
                Action = action,
                Timestamp = startTime,
                Success = false,
                ErrorMessage = ex.Message
            }, cancellationToken);
            
            throw;
        }
    }
}
```

## Security Considerations

### Admin Account Security
- Enforce strong passwords for admin accounts
- Require 2FA for all admin users
- Limit number of super admin accounts
- Regular password rotation
- Monitor admin activity closely

### Access Control
- Implement principle of least privilege
- Regular permission audits
- Time-limited admin sessions
- IP whitelisting for admin access (optional)

### Audit Trail
- Log all admin actions
- Immutable audit logs
- Regular audit log reviews
- Alert on suspicious activities

## Monitoring & Alerts

### Key Metrics
- Failed admin login attempts
- Permission changes
- User creation/deletion
- Role assignments
- Security setting modifications

### Alert Configuration
```csharp
public class AdminAlertSettings
{
    public bool AlertOnFailedLoginAttempts { get; set; } = true;
    public int FailedLoginThreshold { get; set; } = 3;
    
    public bool AlertOnRoleChanges { get; set; } = true;
    public bool AlertOnSecuritySettingChanges { get; set; } = true;
    public bool AlertOnBulkUserActions { get; set; } = true;
    
    public List<string> AlertRecipients { get; set; } = new();
}
```

## Best Practices

### ✅ Do's
- Implement role-based access control
- Log all administrative actions
- Enforce 2FA for admins
- Regular security audits
- Implement IP whitelisting for production
- Use separate admin interface (optional)
- Monitor admin activity in real-time

### ❌ Don'ts
- Don't share admin credentials
- Don't use generic admin accounts
- Don't skip audit logging
- Don't grant admin access casually
- Don't ignore security alerts

## Related Documentation
- [Identity Module](README.md)
- [User Management API](../../USER_MANAGEMENT_API.md)
- [Admin Management Implementation](../../ADMIN_MANAGEMENT_IMPLEMENTATION.md)
