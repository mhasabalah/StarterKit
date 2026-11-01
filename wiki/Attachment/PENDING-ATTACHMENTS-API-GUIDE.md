# Pending Attachments API Quick Reference

## API Endpoints

### 1. Upload File to Session (Anonymous)

**Endpoint:** `POST /api/v1/attachments/pending/upload`

**Authentication:** None (Anonymous)

**Rate Limit:** 5 uploads per minute per IP

**Request:**
```http
POST /api/v1/attachments/pending/upload
Content-Type: multipart/form-data

--boundary
Content-Disposition: form-data; name="file"; filename="house1.jpg"
Content-Type: image/jpeg

[binary data]
--boundary
Content-Disposition: form-data; name="sessionId"

00000000-0000-0000-0000-000000000001
--boundary
Content-Disposition: form-data; name="category"

PropertyImage
--boundary
Content-Disposition: form-data; name="displayOrder"

0
--boundary--
```

**cURL Example:**
```bash
curl -X POST http://localhost:5000/api/v1/attachments/pending/upload \
  -F "file=@house1.jpg" \
  -F "sessionId=00000000-0000-0000-0000-000000000001" \
  -F "category=PropertyImage" \
  -F "displayOrder=0"
```

**Response:**
```json
{
  "success": true,
  "message": "File uploaded successfully to session",
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "sessionId": "00000000-0000-0000-0000-000000000001",
    "fileName": "550e8400-e29b-41d4-a716-446655440000_house1.jpg",
    "fileUrl": "/uploads/temp/00000000-0000-0000-0000-000000000001/550e8400-e29b-41d4-a716-446655440000.jpg",
    "expiresAt": "2026-02-03T12:00:00Z",
    "displayOrder": 0
  }
}
```

### 2. Get Session Attachments

**Endpoint:** `GET /api/v1/attachments/pending/session/{sessionId}`

**Authentication:** None (Anonymous)

**Request:**
```http
GET /api/v1/attachments/pending/session/00000000-0000-0000-0000-000000000001
```

**cURL Example:**
```bash
curl http://localhost:5000/api/v1/attachments/pending/session/00000000-0000-0000-0000-000000000001
```

**Response:**
```json
{
  "success": true,
  "message": "Pending attachments retrieved successfully",
  "data": {
    "attachments": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440000",
        "sessionId": "00000000-0000-0000-0000-000000000001",
        "fileName": "550e8400-e29b-41d4-a716-446655440000_house1.jpg",
        "originalFileName": "house1.jpg",
        "contentType": "image/jpeg",
        "fileSize": 1048576,
        "fileUrl": "/uploads/temp/00000000-0000-0000-0000-000000000001/550e8400-e29b-41d4-a716-446655440000.jpg",
        "thumbnailUrl": "/uploads/temp/00000000-0000-0000-0000-000000000001/thumb_550e8400-e29b-41d4-a716-446655440000.jpg",
        "type": "Image",
        "category": "PropertyImage",
        "displayOrder": 0,
        "width": 200,
        "height": 200,
        "expiresAt": "2026-02-03T12:00:00Z",
        "createdAt": "2026-02-01T12:00:00Z",
        "isExpired": false
      }
    ],
    "totalCount": 1,
    "remainingSlots": 19
  }
}
```

### 3. Associate Files with Property (Authenticated)

**Endpoint:** `POST /api/v1/attachments/pending/associate`

**Authentication:** Required (Bearer token)

**Request:**
```http
POST /api/v1/attachments/pending/associate
Authorization: Bearer {your-jwt-token}
Content-Type: application/json

{
  "sessionId": "00000000-0000-0000-0000-000000000001",
  "entityType": "Property",
  "entityId": "123e4567-e89b-12d3-a456-426614174000"
}
```

**cURL Example:**
```bash
curl -X POST http://localhost:5000/api/v1/attachments/pending/associate \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sessionId": "00000000-0000-0000-0000-000000000001",
    "entityType": "Property",
    "entityId": "123e4567-e89b-12d3-a456-426614174000"
  }'
```

**Response:**
```json
{
  "success": true,
  "message": "Successfully associated 5 attachments",
  "data": {
    "totalAssociated": 5,
    "attachmentIds": [
      "550e8400-e29b-41d4-a716-446655440000",
      "550e8400-e29b-41d4-a716-446655440001",
      "550e8400-e29b-41d4-a716-446655440002",
      "550e8400-e29b-41d4-a716-446655440003",
      "550e8400-e29b-41d4-a716-446655440004"
    ]
  }
}
```

### 4. Delete Pending Attachment

**Endpoint:** `DELETE /api/v1/attachments/pending/{id}?sessionId={sessionId}`

**Authentication:** None (Anonymous)

**Request:**
```http
DELETE /api/v1/attachments/pending/550e8400-e29b-41d4-a716-446655440000?sessionId=00000000-0000-0000-0000-000000000001
```

**cURL Example:**
```bash
curl -X DELETE "http://localhost:5000/api/v1/attachments/pending/550e8400-e29b-41d4-a716-446655440000?sessionId=00000000-0000-0000-0000-000000000001"
```

**Response:**
```json
{
  "success": true,
  "message": "Pending attachment deleted successfully",
  "data": true
}
```

## Frontend Integration Example (JavaScript)

### Generate Session ID
```javascript
// Generate once at the start of property creation flow
const sessionId = crypto.randomUUID();
localStorage.setItem('propertyUploadSession', sessionId);
```

### Upload Files
```javascript
async function uploadFile(file, displayOrder = 0) {
  const sessionId = localStorage.getItem('propertyUploadSession');

  const formData = new FormData();
  formData.append('file', file);
  formData.append('sessionId', sessionId);
  formData.append('category', 'PropertyImage');
  formData.append('displayOrder', displayOrder);

  const response = await fetch('/api/v1/attachments/pending/upload', {
    method: 'POST',
    body: formData
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.message);
  }

  return await response.json();
}

// Handle file input
document.getElementById('fileInput').addEventListener('change', async (e) => {
  const files = Array.from(e.target.files);

  for (let i = 0; i < files.length; i++) {
    try {
      const result = await uploadFile(files[i], i);
      console.log('Uploaded:', result.data);
    } catch (error) {
      console.error('Upload failed:', error);
    }
  }
});
```

### Get Session Files
```javascript
async function getSessionFiles() {
  const sessionId = localStorage.getItem('propertyUploadSession');

  const response = await fetch(`/api/v1/attachments/pending/session/${sessionId}`);
  const result = await response.json();

  return result.data; // { attachments, totalCount, remainingSlots }
}
```

### Associate Files After Property Creation
```javascript
async function createPropertyWithPhotos(propertyData) {
  const sessionId = localStorage.getItem('propertyUploadSession');

  // 1. Create property
  const propertyResponse = await fetch('/api/v1/properties', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${authToken}`
    },
    body: JSON.stringify(propertyData)
  });

  const property = await propertyResponse.json();

  // 2. Associate uploaded files
  const associateResponse = await fetch('/api/v1/attachments/pending/associate', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${authToken}`
    },
    body: JSON.stringify({
      sessionId: sessionId,
      entityType: 'Property',
      entityId: property.data.id
    })
  });

  const associateResult = await associateResponse.json();
  console.log(`Associated ${associateResult.data.totalAssociated} photos`);

  // 3. Clear session
  localStorage.removeItem('propertyUploadSession');

  return property;
}
```

## Error Handling

### Session Limit Exceeded (400)
```json
{
  "success": false,
  "message": "Session 00000000-0000-0000-0000-000000000001 has exceeded the maximum allowed uploads (20).",
  "errors": []
}
```

### Rate Limit Exceeded (429)
```json
{
  "success": false,
  "message": "Too many requests. Please try again later.",
  "errors": []
}
```

### File Not Found (404)
```json
{
  "success": false,
  "message": "PendingAttachment with id 550e8400-e29b-41d4-a716-446655440000 not found.",
  "errors": []
}
```

### Unauthorized (401)
```json
{
  "success": false,
  "message": "User must be authenticated",
  "errors": []
}
```

## Limits and Constraints

- **Max files per session:** 20
- **Max uploads per minute (per IP):** 5
- **File expiration:** 48 hours
- **Max file size:** 500MB (hard limit in endpoint)
- **Supported file types:** Images, Documents, Videos, Audio
- **Storage paths:**
  - Temp: `temp/{sessionId}/{fileId}.ext`
  - Permanent: `properties/{propertyId}/{fileId}.ext`

## Workflow Diagram

```
┌─────────────┐
│   Client    │
│ (Anonymous) │
└──────┬──────┘
       │
       │ 1. Generate sessionId (UUID)
       │
       ▼
┌─────────────────────────────────┐
│  Upload files (1-20 max)        │
│  POST /pending/upload           │
│  Rate limit: 5/min per IP       │
└────────┬────────────────────────┘
         │
         │ 2. Files stored in temp/{sessionId}/
         │    Thumbnails generated async
         │
         ▼
┌─────────────────────────────────┐
│  View uploaded files            │
│  GET /pending/session/{id}      │
└────────┬────────────────────────┘
         │
         │ 3. User authenticates
         │    Creates property
         │
         ▼
┌─────────────────────────────────┐
│  Associate files to property    │
│  POST /pending/associate        │
│  (Requires auth)                │
└────────┬────────────────────────┘
         │
         │ 4. Files copied from temp/ to properties/
         │    PendingAttachment marked as associated
         │
         ▼
┌─────────────────────────────────┐
│  Property has photos attached   │
│  PendingAttachments cleaned up  │
│  after 48h by Hangfire job      │
└─────────────────────────────────┘
```

## Hangfire Dashboard

Access background job monitoring at: `/hangfire`

**Jobs to monitor:**
- `cleanup-expired-attachments` - Runs every 6 hours
- Thumbnail generation jobs - On-demand per upload

## Testing with Postman

1. Import the collection using the endpoint definitions above
2. Set environment variables:
   - `baseUrl`: `http://localhost:5000`
   - `sessionId`: `00000000-0000-0000-0000-000000000001`
   - `authToken`: Your JWT token

3. Test flow:
   - Upload 3 files
   - Get session files
   - Delete one file
   - Create property (separate API)
   - Associate files
   - Verify files moved to property

## Production Checklist

- [ ] Configure Hangfire dashboard authorization
- [ ] Set up CORS for trusted origins
- [ ] Configure rate limiting per environment
- [ ] Set up monitoring for orphaned files
- [ ] Configure alerts for cleanup job failures
- [ ] Test with Azure Blob Storage (if using)
- [ ] Verify database indexes are created
- [ ] Test cleanup job execution
- [ ] Load test rate limiting
- [ ] Monitor thumbnail generation queue
