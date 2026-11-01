using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Configuration;

/// <summary>
/// Validates AttachmentOptions configuration
/// </summary>
public class AttachmentOptionsValidator : IValidateOptions<AttachmentOptions>
{
    public ValidateOptionsResult Validate(string? name, AttachmentOptions options)
    {
        var errors = new List<string>();

        // Validate Storage options
        if (options.Storage.Provider == StorageProvider.AzureBlob)
        {
            if (string.IsNullOrWhiteSpace(options.Storage.AzureBlob.ConnectionString))
            {
                errors.Add("Azure Blob Storage connection string is required when Provider is AzureBlob");
            }

            if (string.IsNullOrWhiteSpace(options.Storage.AzureBlob.ContainerName))
            {
                errors.Add("Azure Blob Storage container name is required");
            }
        }

        if (options.Storage.Provider == StorageProvider.Local)
        {
            if (string.IsNullOrWhiteSpace(options.Storage.Local.UploadPath))
            {
                errors.Add("Local storage upload path is required when Provider is Local");
            }
        }

        // Validate Validation options
        if (options.Validation.MaxFileSizeBytes <= 0)
        {
            errors.Add("Maximum file size must be greater than zero");
        }

        if (options.Validation.MaxImageSizeBytes <= 0)
        {
            errors.Add("Maximum image size must be greater than zero");
        }

        if (options.Validation.MaxDocumentSizeBytes <= 0)
        {
            errors.Add("Maximum document size must be greater than zero");
        }

        if (options.Validation.AllowedExtensions == null || options.Validation.AllowedExtensions.Length == 0)
        {
            errors.Add("At least one allowed file extension must be specified");
        }

        if (options.Validation.AllowedImageTypes == null || options.Validation.AllowedImageTypes.Length == 0)
        {
            errors.Add("At least one allowed image MIME type must be specified");
        }

        if (options.Validation.AllowedDocumentTypes == null || options.Validation.AllowedDocumentTypes.Length == 0)
        {
            errors.Add("At least one allowed document MIME type must be specified");
        }

        // Validate Image options
        if (options.Image.EnableThumbnailGeneration)
        {
            if (options.Image.ThumbnailWidth <= 0)
            {
                errors.Add("Thumbnail width must be greater than zero");
            }

            if (options.Image.ThumbnailHeight <= 0)
            {
                errors.Add("Thumbnail height must be greater than zero");
            }

            if (options.Image.ThumbnailQuality < 1 || options.Image.ThumbnailQuality > 100)
            {
                errors.Add("Thumbnail quality must be between 1 and 100");
            }
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
