# Attachment Module - Implementation Guide

> **Historical.** Upload validation, authorization (entity access policies), the download endpoint and storage consistency changed - [Security.md](Security.md) is authoritative where this page differs.

Complete implementation guide for file upload functionality with best practices.

---

## Upload Endpoint

### Request Model

```csharp
public sealed class UploadAttachmentRequest
{
    [FromForm(Name = "file")]
    public required IFormFile File { get; init; }
    
    [FromForm(Name = "type")]
    public AttachmentType Type { get; init; }
    
    [FromForm(Name = "entityType")]
    public EntityType EntityType { get; init; }
    
    [FromForm(Name = "entityId")]
    public Guid EntityId { get; init; }
    
    [FromForm(Name = "description")]
    public string? Description { get; init; }
    
    [FromForm(Name = "isPublic")]
    public bool IsPublic { get; init; }
    
    [FromForm(Name = "generateThumbnail")]
    public bool GenerateThumbnail { get; init; } = true;
}
```

**Features**:
- ✅ Automatic model binding with `[FromForm]`
- ✅ Type-safe enum and GUID parsing
- ✅ `required` keyword ensures file presence
- ✅ Immutable with `init` properties
- ✅ Default values supported

### Endpoint Implementation

```csharp
app.MapPost(EndpointConfig.BaseApiPath + "/attachments/upload",
    async ([FromForm] UploadAttachmentRequest request, 
           ISender sender,
           IHttpContextService httpContextService,
           CancellationToken cancellationToken) =>
    {
        // 1. Basic validation (fast fail)
        if (request.File == null || request.File.Length == 0)
        {
            return Results.BadRequest(ApiResponseExtensions.BadRequest(
                "File is required and cannot be empty"));
        }

        if (request.File.Length > 500 * 1024 * 1024) // 500MB hard limit
        {
            return Results.BadRequest(ApiResponseExtensions.BadRequest(
                "File size exceeds maximum allowed size of 500MB"));
        }

        // 2. Smart type detection
        var attachmentType = request.Type != default 
            ? request.Type 
            : DetermineAttachmentType(request.File.ContentType);

        // 3. Smart category detection
        var category = DetermineCategory(request.EntityType, request.File.ContentType);

        // 4. Proper resource management
        using var fileStream = request.File.OpenReadStream();

        // 5. Build command
        var command = new UploadAttachmentCommand(
            FileName: request.File.FileName,
            ContentType: request.File.ContentType,
            FileSize: request.File.Length,
            FileStream: fileStream,
            Type: attachmentType,
            Category: category,
            UploadedBy: Guid.Empty, // Retrieved from HTTP context in handler
            EntityType: request.EntityType,
            EntityId: request.EntityId,
            Description: request.Description,
            IsPublic: request.IsPublic,
            GenerateThumbnail: request.GenerateThumbnail && attachmentType == AttachmentType.Image);

        // 6. Execute command
        var result = await sender.Send(command, cancellationToken);

        // 7. Map to response
        var response = new UploadAttachmentResponse(
            result.Id,
            result.FileName,
            result.FileUrl,
            result.ThumbnailUrl,
            result.FileSize,
            result.ContentType);

        return Results.Ok(ApiResponseExtensions.Success(response, "File uploaded successfully"));
    })
    .WithName("UploadAttachment")
    .WithTags("Attachments")
    .DisableAntiforgery()
    .Accepts<UploadAttachmentRequest>("multipart/form-data")
    .Produces<ApiResponse<UploadAttachmentResponse>>(StatusCodes.Status200OK)
    .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
    .RequireAuthorization()
    .HasApiVersion(1.0);
```

### Smart Type Detection

Automatically determines attachment type from MIME type:

```csharp
private static AttachmentType DetermineAttachmentType(string contentType)
{
    return contentType.ToLowerInvariant() switch
    {
        var ct when ct.StartsWith("image/") => AttachmentType.Image,
        var ct when ct.StartsWith("video/") => AttachmentType.Video,
        var ct when ct.StartsWith("audio/") => AttachmentType.Audio,
        "application/pdf" or 
        "application/msword" or 
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
        "application/vnd.ms-excel" or
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => AttachmentType.Document,
        _ => AttachmentType.Other
    };
}
```

### Smart Category Detection

Context-aware category assignment:

```csharp
private static AttachmentCategory DetermineCategory(EntityType entityType, string contentType)
{
    var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    
    return entityType switch
    {
        EntityType.Property when isImage => AttachmentCategory.PropertyImage,
        EntityType.Property => AttachmentCategory.PropertyDocument,
        EntityType.User => AttachmentCategory.UserAvatar,
        EntityType.Product => AttachmentCategory.ProductImage,
        EntityType.Company => AttachmentCategory.CompanyLogo,
        EntityType.ServicePlace => AttachmentCategory.ServicePlaceLogo,
        EntityType.Agent => AttachmentCategory.AgentPhoto,
        _ => AttachmentCategory.General
    };
}
```

---

## Multi-Layer Validation

### Layer 1: Endpoint (Fast Fail)
```csharp
// Check file presence
if (request.File == null || request.File.Length == 0)
    return BadRequest("File is required");

// Check hard size limit
if (request.File.Length > 500 * 1024 * 1024)
    return BadRequest("File exceeds 500MB");
```

### Layer 2: Command Validator (FluentValidation)
```csharp
public class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentCommandValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required")
            .MaximumLength(500).WithMessage("File name too long");
            
        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Content type is required");
            
        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage("File size must be greater than zero");
    }
}
```

### Layer 3: Service Validation (Business Rules)
```csharp
// In UploadAttachmentHandler
var validationResult = await _fileValidationService.ValidateFileAsync(
    command.FileStream,
    command.FileName,
    command.ContentType,
    command.FileSize,
    command.Type,
    cancellationToken);

if (!validationResult.IsValid)
{
    return Result.Failure(validationResult.Errors);
}
```

**FileValidationService checks**:
- MIME type validation
- Content verification (magic bytes)
- Blocked extensions
- Type-specific size limits
- Security checks

---

## Using IHttpContextService

### In Authorization Service

```csharp
public class AttachmentAuthorizationService(
    IAttachmentRepository repository,
    IHttpContextService httpContextService,
    ILogger<AttachmentAuthorizationService> logger)
    : IAttachmentAuthorizationService
{
    public async Task<bool> CanAccessAsync(Guid attachmentId)
    {
        var userId = httpContextService.GetCurrentUserId();
        var attachment = await repository.GetByIdAsync(attachmentId);
        
        // Public files accessible to all
        if (attachment.IsPublic) return true;
        
        // Owner can access
        if (attachment.UploadedBy == userId) return true;
        
        // Check admin role
        if (httpContextService.IsInRole("Admin")) return true;
        
        return false;
    }
}
```

### In Command Handler

```csharp
public class UploadAttachmentHandler(
    IAttachmentRepository repository,
    IStorageService storageService,
    IHttpContextService httpContextService)
    : ICommandHandler<UploadAttachmentCommand, AttachmentFileDto>
{
    public async Task<Result<AttachmentFileDto>> Handle(
        UploadAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        // Get current user
        var userId = httpContextService.GetCurrentUserId();
        var userName = httpContextService.GetCurrentUserName();
        
        // Create attachment with user context
        var attachment = AttachmentFile.Create(
            id: Guid.NewGuid(),
            fileName: command.FileName,
            contentType: command.ContentType,
            fileSize: command.FileSize,
            uploadedBy: userId);
        
        // ... rest of upload logic
    }
}
```

---

## API Usage Examples

### cURL
```bash
curl -X POST "https://localhost:5001/api/attachments/upload" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: multipart/form-data" \
  -F "file=@image.jpg" \
  -F "entityType=Property" \
  -F "entityId=3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -F "description=Property image" \
  -F "isPublic=true" \
  -F "generateThumbnail=true"
```

### JavaScript/TypeScript
```typescript
const formData = new FormData();
formData.append('file', fileInput.files[0]);
formData.append('entityType', 'Property');
formData.append('entityId', propertyId);
formData.append('description', 'Property image');
formData.append('isPublic', 'true');
formData.append('generateThumbnail', 'true');

const response = await fetch('/api/attachments/upload', {
  method: 'POST',
  headers: { 'Authorization': `Bearer ${token}` },
  body: formData
});

const result = await response.json();
if (result.isSuccess) {
  console.log('File URL:', result.data.fileUrl);
  console.log('Thumbnail URL:', result.data.thumbnailUrl);
}
```

### C# HttpClient
```csharp
using var form = new MultipartFormDataContent();
using var fileContent = new StreamContent(File.OpenRead("image.jpg"));

form.Add(fileContent, "file", "image.jpg");
form.Add(new StringContent("Property"), "entityType");
form.Add(new StringContent(propertyId.ToString()), "entityId");
form.Add(new StringContent("true"), "generateThumbnail");

var response = await client.PostAsync("/api/attachments/upload", form);
var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadAttachmentResponse>>();
```

---

## Common Issues & Solutions

### Issue: "Missing required value for property 'File'"

**Cause**: Form field name doesn't match or file not sent correctly.

**Solution**: Ensure field name is exactly `file` (lowercase):
```bash
curl -F "file=@myfile.jpg"  # ✅ Correct
curl -F "File=@myfile.jpg"  # ❌ Wrong - capital F
```

### Issue: DirectoryNotFoundException on Startup

**Cause**: Upload directory doesn't exist.

**Solution**: `UseAttachmentModule()` creates directory automatically. Ensure it's called:
```csharp
app.UseAttachmentModule();  // Must be called in Program.cs
```

### Issue: 401 Unauthorized

**Cause**: Missing or invalid JWT token.

**Solution**: Include valid bearer token:
```bash
curl -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### Issue: File uploaded but URL returns 404

**Cause**: Static file middleware not configured.

**Solution**: `UseAttachmentModule()` configures static files automatically. Check:
1. `UseAttachmentModule()` is called
2. Provider is set to `Local` in appsettings.json
3. Upload directory exists

---

## Testing

### Unit Test: Type Detection
```csharp
[Theory]
[InlineData("image/jpeg", AttachmentType.Image)]
[InlineData("video/mp4", AttachmentType.Video)]
[InlineData("application/pdf", AttachmentType.Document)]
public void DetermineAttachmentType_ReturnsCorrectType(string mimeType, AttachmentType expected)
{
    var result = UploadAttachmentEndPoint.DetermineAttachmentType(mimeType);
    result.Should().Be(expected);
}
```

### Integration Test: Upload
```csharp
[Fact]
public async Task UploadAttachment_ValidRequest_ReturnsSuccess()
{
    // Arrange
    var client = _factory.CreateClient();
    using var form = new MultipartFormDataContent();
    using var fileContent = new ByteArrayContent(TestData.SampleImage);
    
    form.Add(fileContent, "file", "test.jpg");
    form.Add(new StringContent("Property"), "entityType");
    form.Add(new StringContent(Guid.NewGuid().ToString()), "entityId");
    
    // Act
    var response = await client.PostAsync("/api/attachments/upload", form);
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadAttachmentResponse>>();
    result.IsSuccess.Should().BeTrue();
    result.Data.FileUrl.Should().NotBeNullOrEmpty();
}
```

---

## Security Best Practices

1. **Always require authorization**: `.RequireAuthorization()`
2. **Validate file content**: Enable content verification
3. **Block dangerous extensions**: Configure in appsettings.json
4. **Enforce size limits**: Multiple layers (endpoint + service)
5. **Use IHttpContextService**: Get authenticated user context
6. **Validate MIME types**: Type-specific allowed lists
7. **Sanitize file names**: Done automatically in handler

---

## Performance Tips

1. **Use streaming**: Don't buffer entire file in memory
2. **Fail-fast validation**: Check size before reading content
3. **Async I/O**: Use async methods throughout
4. **Dispose resources**: Use `using` statements
5. **CDN for public files**: Set `BaseUrl` in configuration

---

## Summary

✅ **Type-safe request models** with `[FromForm]` binding  
✅ **Multi-layer validation** (endpoint, command, service)  
✅ **Smart detection** of types and categories  
✅ **User context** via `IHttpContextService`  
✅ **Comprehensive error handling** with clear messages  
✅ **Production-ready** with security best practices  
✅ **Well-documented** OpenAPI/Swagger integration  
✅ **Easy to test** with unit and integration tests
