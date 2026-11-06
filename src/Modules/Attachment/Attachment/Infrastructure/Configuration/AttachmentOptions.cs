namespace Attachment.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the Attachment module (section <c>Attachment</c>).
/// </summary>
public class AttachmentOptions
{
    public const string SectionName = "Attachment";

    public PendingAttachmentOptions Pending { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();

    public ValidationOptions Validation { get; set; } = new();

    public ImageOptions Image { get; set; } = new();

    public DownloadOptions Download { get; set; } = new();

    public ReconciliationOptions Reconciliation { get; set; } = new();
}

/// <summary>
/// The DB/blob reconciliation sweeper (<c>AttachmentReconciliationJob</c>, section <c>Attachment:Reconciliation</c>).
/// It removes abandoned uploads, retries failed blob deletions, purges old tombstones and - optionally - deletes
/// blobs that no row references.
/// </summary>
public class ReconciliationOptions
{
    /// <summary>Schedule the recurring sweeper (default true). False removes the recurring job.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hangfire cron expression, UTC (default every 15 minutes).</summary>
    public string Cron { get; set; } = "*/15 * * * *";

    /// <summary>Rows (or storage objects) per batch. Each row is its own unit of work.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Safety stop: batches per phase and run (default 100); the next run continues.</summary>
    public int MaxBatchesPerRun { get; set; } = 100;

    /// <summary>An <c>Uploading</c> row older than this is abandoned: its blob and row are removed (default 60).</summary>
    public int UploadingTimeoutMinutes { get; set; } = 60;

    /// <summary>First retry delay after a failed blob deletion; doubles per attempt (default 5).</summary>
    public int DeletionRetryBaseDelayMinutes { get; set; } = 5;

    /// <summary>Upper bound of the retry delay (default 1440 = 1 day).</summary>
    public int DeletionRetryMaxDelayMinutes { get; set; } = 1440;

    /// <summary>From this many failed attempts on, every failure is logged as an error (default 10).</summary>
    public int DeletionAttemptsAlertThreshold { get; set; } = 10;

    /// <summary>
    /// Soft-deleted and associated pending-upload rows older than this are hard-deleted (default 30).
    /// Their blobs are already gone (deleted) or moved (associated).
    /// </summary>
    public int PurgeRetentionDays { get; set; } = 30;

    public StorageScanOptions StorageScan { get; set; } = new();

    /// <summary>Retry delay after <paramref name="attempts"/> failed deletions: base * 2^(attempts-1), capped.</summary>
    public TimeSpan DeletionRetryDelay(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var minutes = Math.Min((double)DeletionRetryBaseDelayMinutes * Math.Pow(2, exponent), DeletionRetryMaxDelayMinutes);
        return TimeSpan.FromMinutes(minutes);
    }
}

/// <summary>
/// Optional scan of the storage provider for blobs no attachment / pending row references (orphans left by a crash
/// between a blob write and the row insert of a pending upload or an associate move, or by a failed compensation).
/// </summary>
public class StorageScanOptions
{
    /// <summary>Off by default: listing a large bucket costs requests and time.</summary>
    public bool Enabled { get; set; }

    /// <summary>Only log what would be deleted (default true). Set false to really delete orphans.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Blobs modified more recently than this are never touched (default 24 h): a pending upload or an associate
    /// writes the blob shortly before its row.
    /// </summary>
    public int SafetyWindowHours { get; set; } = 24;

    /// <summary>Objects inspected per run (default 10,000); the scan restarts from the beginning next run.</summary>
    public int MaxObjectsPerRun { get; set; } = 10_000;
}

/// <summary>How stored files are handed out to clients.</summary>
public class DownloadOptions
{
    /// <summary>Lifetime of signed read URLs (S3 presigned GET, Azure SAS, Local signed route). Default 5 minutes.</summary>
    public int ReadUrlTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Optional absolute origin (e.g. <c>https://api.example.com</c>) prefixed to the stable download URLs
    /// returned in DTOs. Empty = relative URLs (<c>/api/v1/attachments/{id}/download</c>).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Content types served with <c>Content-Disposition: inline</c>. Everything else is forced to
    /// <c>attachment</c> (download). Keep this to raster image types; never add HTML/SVG/XML.
    /// </summary>
    public string[] InlineContentTypes { get; set; } = [];

    /// <summary>
    /// HMAC key (at least 32 characters) used to sign pending upload session ids and Local read URLs.
    /// When empty, a key is derived from <c>JwtSettings:SecretKey</c>; if that is empty too, a random
    /// per-process key is used (sessions/URLs then do not survive restarts or work across instances).
    /// </summary>
    public string? SigningKey { get; set; }
}

public class StorageOptions
{
    /// <summary>Storage provider type: Local, AzureBlob, S3. The selected provider's settings are required.</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Local;

    public LocalStorageOptions Local { get; set; } = new();

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();
}

public enum StorageProvider
{
    Local,
    AzureBlob,
    S3
}

public class LocalStorageOptions
{
    /// <summary>
    /// Upload directory (relative to the content root, or absolute). Files are NOT served as static files;
    /// they are only reachable through the access-checked download endpoint or a signed read URL.
    /// </summary>
    public string UploadPath { get; set; } = "uploads";

    /// <summary>Obsolete: files are no longer served from a static path. Kept so existing config still binds.</summary>
    public string? BaseUrl { get; set; }
}

public class AzureBlobStorageOptions
{
    /// <summary>Azure storage connection string (must contain an account key so SAS URLs can be signed).</summary>
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "attachments";

    /// <summary>Obsolete: containers are always created private; reads go through SAS URLs.</summary>
    public bool UsePublicAccess { get; set; }
}

public class S3StorageOptions
{
    /// <summary>
    /// AWS Access Key ID. Leave AccessKeyId and SecretAccessKey empty to use the default credential chain
    /// (IAM role / instance profile / environment variables / shared profile).
    /// </summary>
    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string BucketName { get; set; } = "attachments";

    /// <summary>Obsolete: objects are always read through short-lived presigned URLs.</summary>
    public bool UsePublicAccess { get; set; }

    /// <summary>Obsolete: unsigned CloudFront URLs are no longer handed out.</summary>
    public string? CloudFrontDomain { get; set; }

    public bool UseServerSideEncryption { get; set; } = true;

    /// <summary>Storage class (STANDARD, INTELLIGENT_TIERING, GLACIER, etc.)</summary>
    public string StorageClass { get; set; } = "STANDARD";

    /// <summary>Obsolete: use <c>Attachment:Download:ReadUrlTtlMinutes</c>.</summary>
    public int PresignedUrlExpirationMinutes { get; set; } = 60;
}

public class ValidationOptions
{
    /// <summary>Maximum size for files of type Other (default 10MB).</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;

    public long MaxDocumentSizeBytes { get; set; } = 50 * 1024 * 1024;

    public long MaxVideoSizeBytes { get; set; } = 100 * 1024 * 1024;

    public long MaxAudioSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Images larger than this (width * height / 1,000,000) are rejected before decoding
    /// (decompression-bomb guard). Default 40 MP.
    /// </summary>
    public double MaxImageMegapixels { get; set; } = 40;

    /// <summary>
    /// Allowed file extensions. Every allowed extension must also have a content signature rule
    /// (see <c>FileSignatureCatalog</c>); extensions without one are rejected.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [];

    public string[] AllowedImageTypes { get; set; } = [];

    public string[] AllowedDocumentTypes { get; set; } = [];

    public string[] AllowedVideoTypes { get; set; } = [];

    public string[] AllowedAudioTypes { get; set; } = [];

    /// <summary>
    /// Scan uploads with the registered <c>IMalwareScanner</c>. When true and no real scanner is registered,
    /// the application fails at startup.
    /// </summary>
    public bool EnableVirusScanning { get; set; }

    /// <summary>
    /// Refuse to start when <see cref="EnableVirusScanning"/> is false. Off by default; production deployments that
    /// accept uploads from untrusted users should turn it on (without it startup only logs a warning).
    /// </summary>
    public bool RequireVirusScanning { get; set; }

    /// <summary>Built-in scanner adapter selection (<c>Attachment:Validation:Scanner</c>).</summary>
    public MalwareScannerOptions Scanner { get; set; } = new();

    /// <summary>Obsolete: content (magic-byte) verification is always on.</summary>
    public bool EnableContentVerification { get; set; } = true;

    /// <summary>
    /// Extra blocked extensions. A built-in list (html, htm, svg, js, exe, ...) is always blocked, even if an
    /// extension is also listed in <see cref="AllowedExtensions"/>.
    /// </summary>
    public string[] BlockedExtensions { get; set; } = [];

    /// <summary>Largest per-type limit; used for the request body limit of the upload endpoints.</summary>
    public long MaxUploadBytes => new[]
    {
        MaxFileSizeBytes, MaxImageSizeBytes, MaxDocumentSizeBytes, MaxVideoSizeBytes, MaxAudioSizeBytes
    }.Max();
}

public enum MalwareScannerProvider
{
    /// <summary>No built-in adapter: the no-op scanner, or one the host registers itself.</summary>
    None = 0,

    /// <summary>ClamAV daemon (clamd) over TCP, <c>INSTREAM</c> protocol.</summary>
    ClamAv = 1
}

public class MalwareScannerOptions
{
    public MalwareScannerProvider Provider { get; set; } = MalwareScannerProvider.None;

    /// <summary>clamd host name (the compose service is <c>clamav</c>).</summary>
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 3310;

    /// <summary>Connect + scan timeout for one file. Large documents on a busy clamd take seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>INSTREAM chunk size. clamd's <c>StreamMaxLength</c> must be at least the largest upload.</summary>
    public int ChunkSizeBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// When the scanner is unreachable or errors: false (default) rejects the upload with 503
    /// <c>malware_scanner_unavailable</c>; true accepts it unscanned and logs an error. Never true in production
    /// unless an outage is worse for you than an unscanned file.
    /// </summary>
    public bool FailOpen { get; set; }
}

public class ImageOptions
{
    public bool EnableThumbnailGeneration { get; set; } = true;

    public int ThumbnailWidth { get; set; } = 200;

    public int ThumbnailHeight { get; set; } = 200;

    /// <summary>Thumbnail quality (1-100)</summary>
    public int ThumbnailQuality { get; set; } = 80;

    /// <summary>Auto-orient images (EXIF) before generating thumbnails.</summary>
    public bool EnableOptimization { get; set; } = true;
}

/// <summary>Pending attachment options for session-based (pre-entity) uploads.</summary>
public class PendingAttachmentOptions
{
    /// <summary>Maximum number of live uploads per server-issued session (default: 20).</summary>
    public int MaxUploadsPerSession { get; set; } = 20;

    /// <summary>Unused: use the <c>upload</c> rate-limiting policy (RateLimiting:Policies).</summary>
    public int MaxUploadsPerMinute { get; set; } = 5;

    /// <summary>Expiration time in hours for pending attachments (default: 48)</summary>
    public int ExpirationHours { get; set; } = 48;

    /// <summary>Cleanup job interval in hours (default: 6). 24 or more = daily.</summary>
    public int CleanupIntervalHours { get; set; } = 6;

    /// <summary>Rows per cleanup batch (default: 100). The job loops batches until nothing is left.</summary>
    public int CleanupBatchSize { get; set; } = 100;
}

/// <summary>
/// Default list values. Configuration binding APPENDS to non-empty array defaults, which would make it
/// impossible to shrink an allowlist from appsettings; so the properties default to empty and these values
/// are applied (PostConfigure) only when a list was not configured at all.
/// </summary>
public static class AttachmentOptionsDefaults
{
    public static readonly string[] InlineContentTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp"
    ];

    public static readonly string[] AllowedExtensions =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", // Images
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", // Documents
        ".txt", ".csv", ".json", ".xml" // Text
    ];

    public static readonly string[] AllowedImageTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp"
    ];

    public static readonly string[] AllowedDocumentTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain", "text/csv", "application/json", "application/xml"
    ];

    public static readonly string[] AllowedVideoTypes =
    [
        "video/mp4", "video/mpeg", "video/quicktime", "video/x-msvideo"
    ];

    public static readonly string[] AllowedAudioTypes =
    [
        "audio/mpeg", "audio/mp3", "audio/wav", "audio/ogg"
    ];

    public static readonly string[] BlockedExtensions =
    [
        ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".js"
    ];

    /// <summary>Fills every list that is still empty with its default.</summary>
    public static AttachmentOptions ApplyDefaults(this AttachmentOptions options)
    {
        var v = options.Validation;
        if (v.AllowedExtensions.Length == 0)
            v.AllowedExtensions = AllowedExtensions;
        if (v.AllowedImageTypes.Length == 0)
            v.AllowedImageTypes = AllowedImageTypes;
        if (v.AllowedDocumentTypes.Length == 0)
            v.AllowedDocumentTypes = AllowedDocumentTypes;
        if (v.AllowedVideoTypes.Length == 0)
            v.AllowedVideoTypes = AllowedVideoTypes;
        if (v.AllowedAudioTypes.Length == 0)
            v.AllowedAudioTypes = AllowedAudioTypes;
        if (v.BlockedExtensions.Length == 0)
            v.BlockedExtensions = BlockedExtensions;
        if (options.Download.InlineContentTypes.Length == 0)
            options.Download.InlineContentTypes = InlineContentTypes;
        return options;
    }
}
