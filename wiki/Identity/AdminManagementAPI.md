# Admin Management API Reference

## 🔐 Authentication
All endpoints require `SuperAdminOnly` authorization unless specified otherwise.

**Authorization Header**:
```
Authorization: Bearer {your_super_admin_jwt_token}
```

---

## 📋 Endpoints

### 1. Get Admins (Paginated)
**GET** `/api/identity/admins`

**Authorization**: `SuperAdminOnly`

**Query Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| pageNumber | int | No | Page number (default: 1) |
| pageSize | int | No | Items per page (default: 10, max: 100) |
| searchTerm | string | No | Search in username, email, name |
| isActive | bool | No | Filter by active status |
| isLocked | bool | No | Filter by lockout status |
| isSuperAdmin | bool | No | Filter by SuperAdmin status |
| sortBy | enum | No | UserName, Email, FullName, CreatedAt, LastLoginAt, LastAdminActionAt |
| sortDescending | bool | No | Sort descending (default: true) |

**Example**:
```http
GET /api/identity/admins?searchTerm=john&isActive=true&pageSize=20
```

**Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "pageIndex": 0,
    "pageSize": 20,
    "count": 5,
    "data": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440000",
        "userName": "john.admin",
        "email": "john@admin.com",
        "fullName": "John Admin",
        "roles": ["User", "Admin"],
        "isSuperAdmin": false,
        "isActive": true,
        "isLocked": false,
        "createdAt": "2026-01-01T10:00:00Z",
        "lastLoginAt": "2026-01-09T15:30:00Z"
      }
    ]
  }
}
```

---

### 2. Get Admin By ID
**GET** `/api/identity/admins/{id}`

**Authorization**: `SuperAdminOnly`

**Example**:
```http
GET /api/identity/admins/550e8400-e29b-41d4-a716-446655440000
```

**Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "userName": "john.admin",
    "email": "john@admin.com",
    "fullName": "John Admin",
    "roles": ["User", "Admin"],
    "isSuperAdmin": false,
    "isActive": true,
    "isLocked": false,
    "createdAt": "2026-01-01T10:00:00Z",
    "lastLoginAt": "2026-01-09T15:30:00Z",
    "lastAdminActionAt": "2026-01-09T14:20:00Z"
  }
}
```

---

### 3. Create Admin
**POST** `/api/identity/admins`

**Authorization**: `SuperAdminOnly`

**Request Body**:
```json
{
  "userName": "jane.admin",
  "email": "jane.admin@company.com",
  "password": "SecureP@ss123!",
  "firstName": "Jane",
  "lastName": "Admin",
  "phoneNumber": "+1234567890",
  "isSuperAdmin": false
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| userName | string | Yes | Username (3-50 chars, must be unique) |
| email | string | Yes | Email address (must be valid & unique) |
| password | string | Yes | Password (min 8 chars) |
| firstName | string | No | First name |
| lastName | string | No | Last name |
| phoneNumber | string | No | Phone number |
| isSuperAdmin | bool | Yes | Create as SuperAdmin (true) or Admin (false) |

**Response** (201 Created):
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": {
    "success": true,
    "message": "Admin account created successfully for jane.admin",
    "adminId": "660e8400-e29b-41d4-a716-446655440000",
    "roles": ["User", "Admin"]
  }
}
```

**Error Responses**:
- **400 Bad Request**: Username/email already exists, validation failed
- **403 Forbidden**: Not SuperAdmin

---

### 4. Assign Role to User
**POST** `/api/identity/users/{id}/roles`

**Authorization**: `AdminOnly` (SuperAdminOnly for Admin/SuperAdmin roles)

**Request Body**:
```json
{
  "roleName": "Owner",
  "reason": "User completed business verification"
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| roleName | string | Yes | User, Owner, Admin, SuperAdmin |
| reason | string | No | Optional reason for assignment |

**Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "Role 'Owner' assigned successfully. Reason: User completed...",
    "roleName": "Owner"
  }
}
```

**Error Responses**:
- **400 Bad Request**: User already has role, invalid role
- **403 Forbidden**: Insufficient permissions
- **404 Not Found**: User not found

---

### 5. Revoke Role from User
**DELETE** `/api/identity/users/{id}/roles/{roleName}`

**Authorization**: `AdminOnly` (SuperAdminOnly for Admin/SuperAdmin roles)

**Request Body**:
```json
{
  "reason": "Role no longer needed"
}
```

**Example**:
```http
DELETE /api/identity/users/550e8400-e29b-41d4-a716-446655440000/roles/Owner
```

**Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "Role 'Owner' revoked successfully. Reason: Role no longer needed",
    "roleName": "Owner"
  }
}
```

**Error Responses**:
- **400 Bad Request**: User doesn't have role, cannot revoke own SuperAdmin
- **403 Forbidden**: Insufficient permissions

---

### 6. Promote User to Admin
**POST** `/api/identity/users/{id}/promote-admin`

**Authorization**: `SuperAdminOnly`

**Request Body**:
```json
{
  "makeSuperAdmin": false,
  "reason": "Promoted for customer support team leadership"
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| makeSuperAdmin | bool | Yes | Create as SuperAdmin (true) or Admin (false) |
| reason | string | Yes | Reason for promotion (10-500 chars) |

**Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "User promoted to Admin successfully. Reason: Promoted for...",
    "newRoles": ["User", "Admin"]
  }
}
```

**Error Responses**:
- **400 Bad Request**: User is already admin
- **404 Not Found**: User not found

---

### 7. Demote Admin to User
**POST** `/api/identity/admins/{id}/demote`

**Authorization**: `SuperAdminOnly`

**Request Body**:
```json
{
  "reason": "Admin responsibilities transferred to another team member"
}
```

**Body Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| reason | string | Yes | Reason for demotion (10-500 chars) |

**Response** (200 OK):
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "success": true,
    "message": "Admin demoted to regular user successfully. Reason: Admin responsibilities...",
    "remainingRoles": ["User"]
  }
}
```

**Error Responses**:
- **400 Bad Request**: User is not admin, cannot demote self
- **404 Not Found**: Admin not found

---

## 🔄 Common Use Cases

### Scenario 1: Create new admin for customer support
```bash
# 1. Create admin account
POST /api/identity/admins
{
  "userName": "support.admin",
  "email": "support@company.com",
  "password": "SecureP@ss123!",
  "isSuperAdmin": false
}

# 2. Verify admin can login
POST /api/identity/login
{
  "identifier": "support@company.com",
  "password": "SecureP@ss123!"
}
```

### Scenario 2: Grant Owner role to verified business
```bash
# 1. Verify user exists
GET /api/identity/users/{id}

# 2. Assign Owner role
POST /api/identity/users/{id}/roles
{
  "roleName": "Owner",
  "reason": "Business verification completed - License #12345"
}
```

### Scenario 3: Promote user to admin
```bash
# 1. Check user is not already admin
GET /api/identity/users/{id}

# 2. Promote to admin
POST /api/identity/users/{id}/promote-admin
{
  "makeSuperAdmin": false,
  "reason": "Excellent performance, promoted to team lead"
}
```

### Scenario 4: Demote admin (leaving team)
```bash
# 1. Verify admin exists
GET /api/identity/admins/{id}

# 2. Demote to regular user
POST /api/identity/admins/{id}/demote
{
  "reason": "Employee left the company - offboarding process"
}
```

---

## 🎯 Role Assignment Matrix

| From \ To | User | Owner | Admin | SuperAdmin |
|-----------|------|-------|-------|------------|
| **User** | — | Admin | SuperAdmin | SuperAdmin |
| **Owner** | Admin | — | SuperAdmin | SuperAdmin |
| **Admin** | SuperAdmin | SuperAdmin | — | SuperAdmin |
| **SuperAdmin** | SuperAdmin | SuperAdmin | SuperAdmin | — |

*Table shows minimum required role to perform assignment*

---

## 🛡️ Security Notes

1. **SuperAdmin Required for**:
   - Creating admins
   - Assigning Admin/SuperAdmin roles
   - Revoking Admin/SuperAdmin roles
   - Promoting users to admin
   - Demoting admins

2. **Admin Can**:
   - Assign User/Owner roles
   - Revoke User/Owner roles
   - View all users
   - Lock/unlock users

3. **Self-Protection Rules**:
   - Cannot demote yourself
   - Cannot revoke your own SuperAdmin role
   - Cannot delete admin accounts

4. **Audit Trail**:
   - All operations logged via domain events
   - Reason tracking for major operations
   - Admin accountability (who performed action)

---

## 📊 Response Codes

| Code | Description |
|------|-------------|
| 200 | Success |
| 201 | Created (new admin) |
| 400 | Bad Request (validation, duplicate, invalid operation) |
| 401 | Unauthorized (not authenticated) |
| 403 | Forbidden (insufficient permissions) |
| 404 | Not Found (user/admin/role not found) |
| 500 | Internal Server Error |

---

## 🧪 Testing with cURL

### Create Admin
```bash
curl -X POST "https://localhost:5001/api/identity/admins" \
  -H "Authorization: Bearer YOUR_SUPER_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userName":"test.admin",
    "email":"test@admin.com",
    "password":"TestP@ss123!",
    "isSuperAdmin":false
  }'
```

### Assign Role
```bash
curl -X POST "https://localhost:5001/api/identity/users/USER_ID/roles" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"roleName":"Owner","reason":"Verified business"}'
```

### Promote to Admin
```bash
curl -X POST "https://localhost:5001/api/identity/users/USER_ID/promote-admin" \
  -H "Authorization: Bearer YOUR_SUPER_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"makeSuperAdmin":false,"reason":"Team lead promotion"}'
```

---

**Version**: 1.0  
**Last Updated**: 2026-01-09  
**Base URL**: `https://localhost:5001` (Development)

