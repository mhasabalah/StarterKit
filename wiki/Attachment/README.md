# Attachment Module

The **Attachment** module provides production-ready file upload, storage, and management capabilities for the StarterKit platform.

## Quick Start

### 1. Configuration (appsettings.json)
```json
{
  "Attachment": {
    "Storage": { "Provider": "Local", "Local": { "UploadPath": "uploads" } },
    "Validation": { "MaxImageSizeBytes": 10485760 },
    "Image": { "EnableThumbnailGeneration": true }
  }
}
```

### 2. Upload a File
```bash
curl -X POST "/api/attachments/upload" \
  -H "Authorization: Bearer {token}" \
  -F "file=@image.jpg" \
  -F "entityType=Property" \
  -F "entityId={guid}"
```

### 3. Response
```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "fileUrl": "/uploads/2024/01/file.jpg",
    "thumbnailUrl": "/uploads/2024/01/file_thumb.jpg"
  }
}
```

## Features

✅ **Multi-storage support**: Local, Azure Blob Storage  
✅ **Automatic thumbnails**: For images with configurable dimensions  
✅ **Type detection**: Smart MIME type and category detection  
✅ **Security**: Content verification, blocked extensions, size limits  
✅ **Validation**: Multi-layer validation (endpoint, command, service)  
✅ **Authorization**: User context via `IHttpContextService`  
✅ **Options Pattern**: Strongly-typed, validated configuration  

## Technology Stack

- **Azure Blob Storage**: Cloud file storage (production)
- **ImageSharp**: Image processing and thumbnail generation
- **MediatR**: CQRS pattern for commands
- **Carter**: Minimal API endpoints
- **FluentValidation**: Multi-layer validation

## Domain Model

### AttachmentFile Entity
```csharp
public class AttachmentFile : BaseEntity
{
    public string FileName { get; private set; }
    public string OriginalFileName { get; private set; }
    public string ContentType { get; private set; }
    public long FileSize { get; private set; }
    public string StoragePath { get; private set; }
    public string? ThumbnailPath { get; private set; }
    
    public AttachmentType Type { get; private set; }
    public AttachmentCategory Category { get; private set; }
    
    public Guid UploadedBy { get; private set; }
    public EntityType? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }
    public bool IsPublic { get; private set; }
    
    // Factory method
    public static AttachmentFile Create(
        Guid id, string fileName, string contentType, 
        long fileSize, string storagePath, AttachmentType type, Guid uploadedBy)
    {
        var attachment = new AttachmentFile { /* ... */ };
        attachment.AddDomainEvent(new AttachmentUploadedEvent(attachment));
        return attachment;
    }
    
    // Domain methods
    public void LinkToEntity(EntityType entityType, Guid entityId) { /* ... */ }
    public void SetThumbnail(string thumbnailPath, int width, int height) { /* ... */ }
}
```

### Enumerations
```csharp
public enum AttachmentType { Image, Document, Video, Audio, Other }
public enum AttachmentCategory { PropertyImage, PropertyDocument, UserAvatar, ProductImage, CompanyLogo, ServicePlaceLogo, AgentPhoto, General }
public enum EntityType { Property, User, Product, Company, ServicePlace, Agent }
```

## API Endpoints

### Upload File
**POST** `/api/attachments/upload`

**Request**: `multipart/form-data`
- `file` (required): File binary
- `entityType`: Property, User, Product, etc.
- `entityId`: GUID
- `description`: Optional description
- `isPublic`: true/false
- `generateThumbnail`: true/false (default: true)

**Response**:
```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "fileName": "image.jpg",
    "fileUrl": "/uploads/2024/01/image.jpg",
    "thumbnailUrl": "/uploads/2024/01/image_thumb.jpg",
    "fileSize": 2048576,
    "contentType": "image/jpeg"
  }
}
```

### Get Attachment by ID
**GET** `/api/attachments/{id}`

### Download File
**GET** `/api/attachments/{id}/download`

### Delete Attachment
**DELETE** `/api/attachments/{id}`

### Get Attachments by Entity
**GET** `/api/attachments/entity/{entityType}/{entityId}`

## Configuration

See [Configuration.md](Configuration.md) for complete configuration options.

**Quick config**:
```json
{
  "Attachment": {
    "Storage": { "Provider": "Local" },
    "Validation": { "MaxImageSizeBytes": 10485760 },
    "Image": { "ThumbnailWidth": 200, "ThumbnailHeight": 200 }
  }
}
```

## Implementation Guide

See [Implementation.md](Implementation.md) for:
- Upload endpoint details
- Multi-layer validation
- Using IHttpContextService
- Smart type/category detection
- API usage examples
- Testing strategies

## Security

✅ **Authorization required**: All endpoints require authentication  
✅ **Content verification**: Validates file content matches extension  
✅ **Blocked extensions**: `.exe`, `.dll`, `.bat`, `.cmd`, `.sh`, `.ps1`, etc.  
✅ **Size limits**: Configurable per file type  
✅ **MIME validation**: Type-specific allowed lists  
✅ **User context**: Tracks uploader via `IHttpContextService`

## Related Documentation

- [Configuration Guide](Configuration.md) - Complete configuration options
- [Implementation Guide](Implementation.md) - Upload endpoint and API usage
- [HttpContext Service](../Shared/HttpContextService-Summary.md) - User context handling
