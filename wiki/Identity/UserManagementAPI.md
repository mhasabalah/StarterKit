# User Management API Reference

## 🔐 Authentication
All endpoints require `AdminOnly` or `SuperAdminOnly` authorization.

**Authorization Header**:
```
Authorization: Bearer {your_admin_jwt_token}
```

---

## 📋 Endpoints

### 1. Get Users (Paginated)
**GET** `/api/identity/users`

**Authorization**: `AdminOnly` (Admin, SuperAdmin)

**Query Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| pageNumber | int | No | Page number (default: 1) |
| pageSize | int | No | Items per page (default: 10, max: 100) |
| searchTerm | string | No | Search in username, email, name, phone (min 2 chars) |
| isActive | bool | No | Filter by active status |
| isLocked | bool | No | Filter by lockout status |
| emailConfirmed | bool | No | Filter by email confirmation |
| role | string | No | Filter by role (User, Owner, Admin, SuperAdmin) |
| oAuthProvider | string | No | Filter by OAuth provider (Google, Facebook, None) |
| createdFrom | datetime | No | Filter created date from |
| createdTo | datetime | No | Filter created date to |
| lastLoginFrom | datetime | No | Filter last login from |
| lastLoginTo | datetime | No | Filter last login to |
| sortBy | enum | No | UserName, Email, FullName, CreatedAt, LastLoginAt |
| sortDescending | bool | No | Sort descending (default: true) |

**Example Request**:
```http
GET /api/identity/users?searchTerm=john&isActive=true&pageNumber=1&pageSize=20&sortBy=CreatedAt
```

**Success Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "pageIndex": 0,
    "pageSize": 20,
    "count": 145,
    "data": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440000",
        "userName": "john.doe",
        "email": "john@example.com",
        "phoneNumber": "+1234567890",
        "fullName": "John Doe",
        "profilePictureUrl": "https://...",
        "isActive": true,
        "isLocked": false,
        "emailConfirmed": true,
        "roles": ["User", "Owner"],
        "createdAt": "2026-01-01T10:00:00Z",
        "lastLoginAt": "2026-01-09T15:30:00Z"
      }
    ]
  }
}
```

---

### 2. Get User By ID
**GET** `/api/identity/users/{id}`

**Authorization**: `AdminOnly` (Admin, SuperAdmin)

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | guid | Yes | User ID |

**Example Request**:
```http
GET /api/identity/users/550e8400-e29b-41d4-a716-446655440000
```

**Success Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "userName": "john.doe",
    "email": "john@example.com",
    "phoneNumber": "+1234567890",
    "firstName": "John",
    "lastName": "Doe",
    "fullName": "John Doe",
    "profilePictureUrl": "https://...",
    "oAuthProvider": "Google",
    "isActive": true,
    "isLocked": false,
    "lockoutEnd": null,
    "emailConfirmed": true,
    "phoneNumberConfirmed": true,
    "twoFactorEnabled": false,
    "accessFailedCount": 0,
    "isProfileComplete": true,
    "profileCompletedAt": "2026-01-01T10:30:00Z",
    "roles": ["User", "Owner"],
    "createdAt": "2026-01-01T10:00:00Z",
    "updatedAt": "2026-01-05T14:20:00Z",
    "lastLoginAt": "2026-01-09T15:30:00Z"
  }
}
```

**Error Response** (404 Not Found):
```json
{
  "isSuccess": false,
  "statusCode": 404,
  "errors": ["User with ID '...' was not found."]
}
```

---

### 3. Lock User
**POST** `/api/identity/users/{id}/lock`

**Authorization**: `AdminOnly` (Admin, SuperAdmin)

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | guid | Yes | User ID to lock |

**Request Body**:
```json
{
  "reason": "Suspicious activity detected - multiple failed login attempts",
  "duration": "OneWeek",
  "customDurationHours": null
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| reason | string | Yes | Reason for locking (10-500 chars) |
| duration | enum | Yes | OneHour, OneDay, OneWeek, OneMonth, Permanent, Custom |
| customDurationHours | int | No | Required if duration is Custom (1-8760 hours) |

**Example Request**:
```http
POST /api/identity/users/550e8400-e29b-41d4-a716-446655440000/lock
Content-Type: application/json

{
  "reason": "Account security review required",
  "duration": "OneWeek"
}
```

**Success Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "User account locked until 2026-01-16 20:00:00 UTC. Reason: Account security review required",
    "lockoutEnd": "2026-01-16T20:00:00Z"
  }
}
```

**Error Responses**:
- **400 Bad Request**: User already locked
- **404 Not Found**: User not found
- **401 Unauthorized**: Not authenticated
- **403 Forbidden**: Not admin

---

### 4. Unlock User
**POST** `/api/identity/users/{id}/unlock`

**Authorization**: `AdminOnly` (Admin, SuperAdmin)

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | guid | Yes | User ID to unlock |

**Request Body**:
```json
{
  "unlockReason": "Security review completed - no issues found"
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| unlockReason | string | No | Optional reason for unlocking |

**Example Request**:
```http
POST /api/identity/users/550e8400-e29b-41d4-a716-446655440000/unlock
Content-Type: application/json

{
  "unlockReason": "False positive - user verified"
}
```

**Success Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "User account unlocked. Reason: False positive - user verified"
  }
}
```

**Error Responses**:
- **400 Bad Request**: User not locked
- **404 Not Found**: User not found

---

### 5. Update User Status
**PATCH** `/api/identity/users/{id}/status`

**Authorization**: `AdminOnly` (Admin, SuperAdmin)

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | guid | Yes | User ID |

**Request Body**:
```json
{
  "isActive": false,
  "reason": "User requested account deactivation"
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| isActive | bool | Yes | Activate (true) or deactivate (false) |
| reason | string | No | Optional reason for status change |

**Example Request**:
```http
PATCH /api/identity/users/550e8400-e29b-41d4-a716-446655440000/status
Content-Type: application/json

{
  "isActive": false,
  "reason": "Temporary deactivation per user request"
}
```

**Success Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "User account deactivated successfully. Reason: Temporary deactivation per user request",
    "newStatus": false
  }
}
```

**Error Responses**:
- **400 Bad Request**: User already has this status
- **404 Not Found**: User not found

---

### 6. Delete User (Soft Delete)
**DELETE** `/api/identity/users/{id}`

**Authorization**: `SuperAdminOnly` (SuperAdmin ONLY)

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | guid | Yes | User ID to delete |

**Request Body**:
```json
{
  "reason": "GDPR right to be forgotten request - Ticket #12345"
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| reason | string | Yes | Reason for deletion (10-500 chars) |

**Example Request**:
```http
DELETE /api/identity/users/550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{
  "reason": "User account terminated - Policy violation #7823"
}
```

**Success Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "User account deleted successfully. Reason: User account terminated - Policy violation #7823"
  }
}
```

**Error Responses**:
- **400 Bad Request**: Cannot delete admin users
- **404 Not Found**: User not found
- **403 Forbidden**: Not super admin

---

## 🔄 Enum Values

### LockDuration
```
OneHour
OneDay
OneWeek
OneMonth
Permanent
Custom
```

### UserSortBy
```
UserName
Email
FullName
CreatedAt
LastLoginAt
```

---

## 🎯 Common Use Cases

### Scenario 1: Find and lock suspicious users
```bash
# 1. Search for users with failed logins
GET /api/identity/users?isLocked=false&searchTerm=suspicious

# 2. Lock the user
POST /api/identity/users/{id}/lock
{
  "reason": "Multiple failed login attempts detected",
  "duration": "OneDay"
}
```

### Scenario 2: Bulk user review
```bash
# 1. Get all inactive users
GET /api/identity/users?isActive=false&pageSize=100

# 2. Review each user and either:
#    - Unlock if legitimate
#    - Delete if confirmed violation
```

### Scenario 3: User support workflow
```bash
# 1. User reports they can't login - check status
GET /api/identity/users/{id}

# 2. If locked, review reason
# 3. If resolved, unlock
POST /api/identity/users/{id}/unlock
{
  "unlockReason": "Support ticket #1234 - User verified via email"
}
```

---

## 🛡️ Security Notes

1. **All endpoints require authentication** - Include `Authorization: Bearer {token}` header
2. **Admin role required** - Only Admin and SuperAdmin can access these endpoints
3. **SuperAdmin for deletion** - Only SuperAdmin can delete users
4. **Admin tracking** - All actions automatically record which admin performed them
5. **Audit trail** - All actions raise domain events for logging
6. **Cannot delete admins** - System prevents deleting admin/super admin users

---

## 📊 Response Codes

| Code | Description |
|------|-------------|
| 200 | Success |
| 400 | Bad Request (validation failed, invalid operation) |
| 401 | Unauthorized (not authenticated) |
| 403 | Forbidden (not admin or insufficient permissions) |
| 404 | Not Found (user not found) |
| 500 | Internal Server Error |

---

## 🧪 Testing with cURL

### Get Users
```bash
curl -X GET "https://localhost:5001/api/identity/users?pageNumber=1&pageSize=10" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

### Lock User
```bash
curl -X POST "https://localhost:5001/api/identity/users/USER_ID/lock" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason":"Test lock","duration":"OneDay"}'
```

### Unlock User
```bash
curl -X POST "https://localhost:5001/api/identity/users/USER_ID/unlock" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"unlockReason":"Test unlock"}'
```

---

**Version**: 1.0  
**Last Updated**: 2026-01-09  
**Base URL**: `https://localhost:5001` (Development)

