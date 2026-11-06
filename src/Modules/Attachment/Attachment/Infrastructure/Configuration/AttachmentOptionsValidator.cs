using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="AttachmentOptions"/> at startup (ValidateOnStart). The selected storage provider's
/// settings are required: there is no silent fallback to Local storage.
/// </summary>
public class AttachmentOptionsValidator : IValidateOptions<AttachmentOptions>
{
    public ValidateOptionsResult Validate(string? name, AttachmentOptions options)
    {
        var errors = new List<string>();

        switch (options.Storage.Provider)
        {
            case StorageProvider.AzureBlob:
                if (string.IsNullOrWhiteSpace(options.Storage.AzureBlob.ConnectionString))
                    errors.Add("Attachment:Storage:AzureBlob:ConnectionString is required when Provider is AzureBlob");
                if (string.IsNullOrWhiteSpace(options.Storage.AzureBlob.ContainerName))
                    errors.Add("Attachment:Storage:AzureBlob:ContainerName is required when Provider is AzureBlob");
                break;

            case StorageProvider.S3:
                if (string.IsNullOrWhiteSpace(options.Storage.S3.BucketName))
                    errors.Add("Attachment:Storage:S3:BucketName is required when Provider is S3");
                if (string.IsNullOrWhiteSpace(options.Storage.S3.Region))
                    errors.Add("Attachment:Storage:S3:Region is required when Provider is S3");
                if (string.IsNullOrWhiteSpace(options.Storage.S3.AccessKeyId) !=
                    string.IsNullOrWhiteSpace(options.Storage.S3.SecretAccessKey))
                    errors.Add("Attachment:Storage:S3: set both AccessKeyId and SecretAccessKey, or neither " +
                               "(neither = default AWS credential chain / IAM role)");
                break;

            case StorageProvider.Local:
                if (string.IsNullOrWhiteSpace(options.Storage.Local.UploadPath))
                    errors.Add("Attachment:Storage:Local:UploadPath is required when Provider is Local");
                break;

            default:
                errors.Add($"Unknown Attachment:Storage:Provider '{options.Storage.Provider}'");
                break;
        }

        var v = options.Validation;
        if (v.MaxFileSizeBytes <= 0 || v.MaxImageSizeBytes <= 0 || v.MaxDocumentSizeBytes <= 0 ||
            v.MaxVideoSizeBytes <= 0 || v.MaxAudioSizeBytes <= 0)
            errors.Add("All Attachment:Validation:Max*SizeBytes values must be greater than zero");

        if (v.MaxImageMegapixels <= 0)
            errors.Add("Attachment:Validation:MaxImageMegapixels must be greater than zero");

        if (v.AllowedExtensions is not { Length: > 0 })
            errors.Add("At least one allowed file extension must be specified");

        if (v.AllowedImageTypes is not { Length: > 0 })
            errors.Add("At least one allowed image MIME type must be specified");

        if (v.AllowedDocumentTypes is not { Length: > 0 })
            errors.Add("At least one allowed document MIME type must be specified");

        if (v.Scanner.Provider == MalwareScannerProvider.ClamAv)
        {
            if (string.IsNullOrWhiteSpace(v.Scanner.Host))
                errors.Add("Attachment:Validation:Scanner:Host is required when Provider is ClamAv");
            if (v.Scanner.Port is < 1 or > 65535)
                errors.Add("Attachment:Validation:Scanner:Port must be between 1 and 65535");
            if (v.Scanner.TimeoutSeconds is < 1 or > 600)
                errors.Add("Attachment:Validation:Scanner:TimeoutSeconds must be between 1 and 600");
            if (v.Scanner.ChunkSizeBytes is < 1024 or > 4 * 1024 * 1024)
                errors.Add("Attachment:Validation:Scanner:ChunkSizeBytes must be between 1024 and 4194304");
        }

        if (options.Image.EnableThumbnailGeneration)
        {
            if (options.Image.ThumbnailWidth <= 0 || options.Image.ThumbnailHeight <= 0)
                errors.Add("Thumbnail width and height must be greater than zero");

            if (options.Image.ThumbnailQuality is < 1 or > 100)
                errors.Add("Thumbnail quality must be between 1 and 100");
        }

        if (options.Download.ReadUrlTtlMinutes is < 1 or > 7 * 24 * 60)
            errors.Add("Attachment:Download:ReadUrlTtlMinutes must be between 1 and 10080");

        if (!string.IsNullOrEmpty(options.Download.SigningKey) && options.Download.SigningKey.Length < 32)
            errors.Add("Attachment:Download:SigningKey must be at least 32 characters");

        if (options.Pending.MaxUploadsPerSession <= 0)
            errors.Add("Attachment:Pending:MaxUploadsPerSession must be greater than zero");

        if (options.Pending.CleanupBatchSize <= 0)
            errors.Add("Attachment:Pending:CleanupBatchSize must be greater than zero");

        if (options.Pending.CleanupIntervalHours <= 0)
            errors.Add("Attachment:Pending:CleanupIntervalHours must be greater than zero");

        var r = options.Reconciliation;
        if (r.Enabled && string.IsNullOrWhiteSpace(r.Cron))
            errors.Add("Attachment:Reconciliation:Cron is required when the sweeper is enabled");

        if (r.BatchSize <= 0 || r.MaxBatchesPerRun <= 0)
            errors.Add("Attachment:Reconciliation:BatchSize and MaxBatchesPerRun must be greater than zero");

        if (r.UploadingTimeoutMinutes < 5)
            errors.Add("Attachment:Reconciliation:UploadingTimeoutMinutes must be at least 5 (uploads in flight)");

        if (r.DeletionRetryBaseDelayMinutes <= 0 || r.DeletionRetryMaxDelayMinutes < r.DeletionRetryBaseDelayMinutes)
            errors.Add("Attachment:Reconciliation:DeletionRetryBaseDelayMinutes must be > 0 and <= DeletionRetryMaxDelayMinutes");

        if (r.PurgeRetentionDays <= 0)
            errors.Add("Attachment:Reconciliation:PurgeRetentionDays must be greater than zero");

        if (r.StorageScan.SafetyWindowHours < 1)
            errors.Add("Attachment:Reconciliation:StorageScan:SafetyWindowHours must be at least 1");

        if (r.StorageScan.MaxObjectsPerRun <= 0)
            errors.Add("Attachment:Reconciliation:StorageScan:MaxObjectsPerRun must be greater than zero");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
