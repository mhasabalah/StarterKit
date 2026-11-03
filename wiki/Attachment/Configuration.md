# Attachment Module - Configuration Guide

> **Updated:** new keys `Download:*` (ReadUrlTtlMinutes, PublicBaseUrl, InlineContentTypes, SigningKey), `Validation:MaxImageMegapixels`; provider settings are validated at startup with no fallback to Local; S3 keys may be empty (IAM role); list options replace (not extend) their defaults; several keys are obsolete. See [Security.md](Security.md) section 8.

Complete configuration guide for the Attachment module using the ASP.NET Core Options Pattern.

---

## Quick Start

### 1. Basic Configuration (appsettings.json)

```json
{
  "Attachment": {
    "Storage": {
      "Provider": "Local",
      "Local": {
        "UploadPath": "uploads",
        "BaseUrl": null
      }
    },
    "Validation": {
      "MaxImageSizeBytes": 10485760,
      "MaxDocumentSizeBytes": 52428800,
      "MaxVideoSizeBytes": 104857600,
      "MaxAudioSizeBytes": 20971520,
      "AllowedImageTypes": ["image/jpeg", "image/png", "image/gif", "image/webp"],
      "AllowedDocumentTypes": ["application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
      "AllowedVideoTypes": ["video/mp4", "video/mpeg"],
      "AllowedAudioTypes": ["audio/mpeg", "audio/wav"],
      "EnableVirusScanning": false,
      "EnableContentVerification": true,
      "BlockedExtensions": [".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".js"]
    },
    "Image": {
      "EnableThumbnailGeneration": true,
      "ThumbnailWidth": 200,
      "ThumbnailHeight": 200,
      "ThumbnailQuality": 80,
      "EnableOptimization": true
    }
  }
}
```

### 2. Module Registration (Program.cs)

```csharp
builder.Services.AddAttachmentModule(builder.Configuration);

// After app build
app.UseAttachmentModule();  // Creates upload directory, configures static files
```

---

## Configuration Sections

### Storage Options

#### Local Storage (Development)
```json
{
  "Storage": {
    "Provider": "Local",
    "Local": {
      "UploadPath": "uploads",
      "BaseUrl": null
    }
  }
}
```

**Features**:
- Files stored in local directory
- Automatic directory creation on startup
- Static file serving configured automatically
- Optional CDN URL via `BaseUrl`

#### Azure Blob Storage (Production)
```json
{
  "Storage": {
    "Provider": "AzureBlob",
    "AzureBlob": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
      "ContainerName": "attachments",
      "UsePublicAccess": true
    }
  }
}
```

**Requirements**:
- Valid Azure Storage connection string
- Container name (created automatically if doesn't exist)
- Public access for direct file URLs

### Validation Options

#### File Size Limits (Bytes)
| Type | Default | Description |
|------|---------|-------------|
| MaxImageSizeBytes | 10 MB | Images (JPG, PNG, GIF, WebP) |
| MaxDocumentSizeBytes | 50 MB | Documents (PDF, Word, Excel) |
| MaxVideoSizeBytes | 100 MB | Videos (MP4, MPEG) |
| MaxAudioSizeBytes | 20 MB | Audio (MP3, WAV) |

#### Security Settings
```json
{
  "Validation": {
    "EnableVirusScanning": false,
    "EnableContentVerification": true,
    "BlockedExtensions": [".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1"]
  }
}
```

**Security Features**:
- **Content Verification**: Validates file content matches extension (prevents `.exe` renamed to `.jpg`)
- **Blocked Extensions**: Always rejected regardless of MIME type
- **Virus Scanning**: Hook for future antivirus integration (ClamAV, Windows Defender)

### Image Processing Options

```json
{
  "Image": {
    "EnableThumbnailGeneration": true,
    "ThumbnailWidth": 200,
    "ThumbnailHeight": 200,
    "ThumbnailQuality": 80,
    "EnableOptimization": true
  }
}
```

**Features**:
- Automatic thumbnail generation for images
- Maintains aspect ratio
- JPEG compression quality (1-100)
- Image optimization (reduces file size)

---

## Environment-Specific Configuration

### Development (appsettings.Development.json)
```json
{
  "Attachment": {
    "Storage": {
      "Provider": "Local"
    },
    "Validation": {
      "MaxImageSizeBytes": 52428800,
      "EnableVirusScanning": false
    }
  }
}
```

### Production (appsettings.Production.json)
```json
{
  "Attachment": {
    "Storage": {
      "Provider": "AzureBlob",
      "AzureBlob": {
        "ConnectionString": "***",
        "ContainerName": "production-attachments"
      }
    },
    "Validation": {
      "MaxImageSizeBytes": 10485760,
      "EnableVirusScanning": true,
      "EnableContentVerification": true
    }
  }
}
```

---

## Usage in Code

### Inject Options
```csharp
public class MyService(IOptions<AttachmentOptions> options)
{
    private readonly AttachmentOptions _options = options.Value;
    
    public void DoSomething()
    {
        var maxSize = _options.Validation.MaxImageSizeBytes;
        var thumbnailWidth = _options.Image.ThumbnailWidth;
    }
}
```

### Access Specific Settings
```csharp
// Storage settings
var provider = _options.Storage.Provider;
var uploadPath = _options.Storage.Local.UploadPath;

// Validation settings
var maxImageSize = _options.Validation.MaxImageSizeBytes;
var allowedTypes = _options.Validation.AllowedImageTypes;

// Image settings
var thumbnailWidth = _options.Image.ThumbnailWidth;
```

---

## Validation Rules

Configuration is validated on application startup using `IValidateOptions<AttachmentOptions>`:

**Storage Validation**:
- ✅ Azure connection string required when `Provider = AzureBlob`
- ✅ Container name required for Azure
- ✅ Upload path required for Local storage

**Validation Options**:
- ✅ All file size limits must be > 0 and < 5GB
- ✅ At least one allowed MIME type per category
- ✅ Thumbnail dimensions must be > 0
- ✅ Quality must be 1-100

**Startup Behavior**:
```csharp
// If configuration is invalid, app fails to start with clear error message
Microsoft.Extensions.Options.OptionsValidationException: 
  Validation failed for AttachmentOptions: 
  'Storage.AzureBlob.ConnectionString is required when Provider is AzureBlob'
```

---

## Testing Configuration

### Unit Tests
```csharp
var options = Microsoft.Extensions.Options.Options.Create(
    new AttachmentOptions
    {
        Validation = new ValidationOptions
        {
            MaxImageSizeBytes = 5 * 1024 * 1024
        }
    });

var service = new FileValidationService(options, logger);
```

### Integration Tests
```csharp
// Options automatically loaded from appsettings.json
var service = _serviceProvider.GetRequiredService<IFileValidationService>();
```

---

## Troubleshooting

### Directory Creation Failed
**Error**: `DirectoryNotFoundException: 'uploads\'`
**Solution**: The `UseAttachmentModule()` middleware automatically creates the directory. Ensure it's called in `Program.cs`.

### Invalid Configuration on Startup
**Error**: `OptionsValidationException`
**Solution**: Check appsettings.json matches the schema. All required fields must be present.

### Azure Blob Connection Failed
**Error**: `Azure.RequestFailedException: Server failed to authenticate`
**Solution**: Verify connection string in appsettings.json. Check Azure Storage account access keys.

---

## Summary

✅ **Strongly-typed** configuration with compile-time safety  
✅ **Validated** on startup with fail-fast behavior  
✅ **Environment-specific** overrides supported  
✅ **Security-first** with blocked extensions and content verification  
✅ **Type-specific** file size limits  
✅ **Easy to test** with `IOptions<T>` mocking  
✅ **Production-ready** with Azure Blob Storage support
