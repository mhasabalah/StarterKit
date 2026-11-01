namespace Attachment.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the Attachment module
/// </summary>
public class AttachmentOptions
{
    public const string SectionName = "Attachment";

    /// <summary>
    /// Pending attachment configuration
    /// </summary>
    public PendingAttachmentOptions Pending { get; set; } = new();

    /// <summary>
    /// Storage configuration
    /// </summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>
    /// File validation configuration
    /// </summary>
    public ValidationOptions Validation { get; set; } = new();

    /// <summary>
    /// Image processing configuration
    /// </summary>
    public ImageOptions Image { get; set; } = new();
}

/// <summary>
/// Storage provider configuration
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// Storage provider type: Local, AzureBlob, S3
    /// </summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Local;
    
    /// <summary>
    /// Local storage configuration
    /// </summary>
    public LocalStorageOptions Local { get; set; } = new();
    
    /// <summary>
    /// Azure Blob storage configuration
    /// </summary>
    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    /// <summary>
    /// AWS S3 storage configuration
    /// </summary>
    public S3StorageOptions S3 { get; set; } = new();
}

/// <summary>
/// Storage provider types
/// </summary>
public enum StorageProvider
{
    Local,
    AzureBlob,
    S3
}

/// <summary>
/// Local file storage options
/// </summary>
public class LocalStorageOptions
{
    /// <summary>
    /// Upload directory path (relative or absolute)
    /// </summary>
    public string UploadPath { get; set; } = "uploads";
    
    /// <summary>
    /// Base URL for serving files
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Azure Blob storage options
/// </summary>
public class AzureBlobStorageOptions
{
    /// <summary>
    /// Azure storage connection string
    /// </summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>
    /// Container name for attachments
    /// </summary>
    public string ContainerName { get; set; } = "attachments";
    
    /// <summary>
    /// Public access level
    /// </summary>
    public bool UsePublicAccess { get; set; } = true;
}

/// <summary>
/// AWS S3 storage options
/// </summary>
public class S3StorageOptions
{
    /// <summary>
    /// AWS Access Key ID
    /// </summary>
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// AWS Secret Access Key
    /// </summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// AWS Region (e.g., us-east-1, eu-west-1)
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// S3 Bucket name for attachments
    /// </summary>
    public string BucketName { get; set; } = "attachments";

    /// <summary>
    /// Use public access (public-read ACL)
    /// </summary>
    public bool UsePublicAccess { get; set; } = true;

    /// <summary>
    /// CloudFront distribution domain (optional, for CDN)
    /// </summary>
    public string? CloudFrontDomain { get; set; }

    /// <summary>
    /// Use server-side encryption (AES256)
    /// </summary>
    public bool UseServerSideEncryption { get; set; } = true;

    /// <summary>
    /// Storage class (STANDARD, INTELLIGENT_TIERING, GLACIER, etc.)
    /// </summary>
    public string StorageClass { get; set; } = "STANDARD";

    /// <summary>
    /// Presigned URL expiration in minutes (for private files)
    /// </summary>
    public int PresignedUrlExpirationMinutes { get; set; } = 60;
}

/// <summary>
/// File validation options
/// </summary>
public class ValidationOptions
{
    /// <summary>
    /// Maximum file size in bytes (default: 10MB)
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    
    /// <summary>
    /// Maximum image size in bytes (default: 10MB)
    /// </summary>
    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;
    
    /// <summary>
    /// Maximum document size in bytes (default: 50MB)
    /// </summary>
    public long MaxDocumentSizeBytes { get; set; } = 50 * 1024 * 1024;
    
    /// <summary>
    /// Maximum video size in bytes (default: 100MB)
    /// </summary>
    public long MaxVideoSizeBytes { get; set; } = 100 * 1024 * 1024;
    
    /// <summary>
    /// Maximum audio size in bytes (default: 20MB)
    /// </summary>
    public long MaxAudioSizeBytes { get; set; } = 20 * 1024 * 1024;
    
    /// <summary>
    /// Allowed file extensions
    /// </summary>
    public string[] AllowedExtensions { get; set; } = 
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", // Images
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", // Documents
        ".txt", ".csv", ".json", ".xml" // Text
    };
    
    /// <summary>
    /// Allowed MIME types for images
    /// </summary>
    public string[] AllowedImageTypes { get; set; } =
    {
        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp"
    };
    
    /// <summary>
    /// Allowed MIME types for documents
    /// </summary>
    public string[] AllowedDocumentTypes { get; set; } =
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain", "text/csv", "application/json", "application/xml"
    };
    
    /// <summary>
    /// Allowed MIME types for videos
    /// </summary>
    public string[] AllowedVideoTypes { get; set; } =
    {
        "video/mp4", "video/mpeg", "video/quicktime", "video/x-msvideo"
    };
    
    /// <summary>
    /// Allowed MIME types for audio
    /// </summary>
    public string[] AllowedAudioTypes { get; set; } =
    {
        "audio/mpeg", "audio/mp3", "audio/wav", "audio/ogg"
    };
    
    /// <summary>
    /// Enable virus scanning (if antivirus service is available)
    /// </summary>
    public bool EnableVirusScanning { get; set; } = false;
    
    /// <summary>
    /// Enable content verification (check actual file content matches extension)
    /// </summary>
    public bool EnableContentVerification { get; set; } = true;
    
    /// <summary>
    /// Blocked file extensions (security)
    /// </summary>
    public string[] BlockedExtensions { get; set; } =
    {
        ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".js"
    };
}

/// <summary>
/// Image processing options
/// </summary>
public class ImageOptions
{
    /// <summary>
    /// Enable automatic thumbnail generation
    /// </summary>
    public bool EnableThumbnailGeneration { get; set; } = true;
    
    /// <summary>
    /// Thumbnail width (maintains aspect ratio)
    /// </summary>
    public int ThumbnailWidth { get; set; } = 200;
    
    /// <summary>
    /// Thumbnail height
    /// </summary>
    public int ThumbnailHeight { get; set; } = 200;
    
    /// <summary>
    /// Thumbnail quality (1-100)
    /// </summary>
    public int ThumbnailQuality { get; set; } = 80;
    
    /// <summary>
    /// Enable image optimization
    /// </summary>
    public bool EnableOptimization { get; set; } = true;
}

/// <summary>
/// Pending attachment options for session-based anonymous uploads
/// </summary>
public class PendingAttachmentOptions
{
    /// <summary>
    /// Maximum number of uploads per session (default: 20)
    /// </summary>
    public int MaxUploadsPerSession { get; set; } = 20;

    /// <summary>
    /// Maximum uploads per minute per IP (default: 5)
    /// </summary>
    public int MaxUploadsPerMinute { get; set; } = 5;

    /// <summary>
    /// Expiration time in hours for pending attachments (default: 48)
    /// </summary>
    public int ExpirationHours { get; set; } = 48;

    /// <summary>
    /// Cleanup job interval in hours (default: 6)
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 6;

    /// <summary>
    /// Batch size for cleanup operations (default: 100)
    /// </summary>
    public int CleanupBatchSize { get; set; } = 100;
}
