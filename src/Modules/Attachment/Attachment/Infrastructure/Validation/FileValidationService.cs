using Attachment.Contracts.Attachments.Dtos;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.ImageProcessing;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Validation;

public class FileValidationService : IFileValidationService
{
    private readonly IImageProcessingService _imageProcessingService;
    private readonly IOptions<AttachmentOptions> _options;
    private readonly ILogger<FileValidationService> _logger;

    public FileValidationService(
        IImageProcessingService imageProcessingService,
        IOptions<AttachmentOptions> options,
        ILogger<FileValidationService> logger)
    {
        _imageProcessingService = imageProcessingService;
        _options = options;
        _logger = logger;
    }

    public async Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        AttachmentType attachmentType,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var validationOptions = _options.Value.Validation;

        // Validate filename
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors.Add("File name is required");
        }
        else
        {
            if (HasInvalidCharacters(fileName))
            {
                errors.Add("File name contains invalid characters");
            }

            // Check for blocked extensions
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (validationOptions.BlockedExtensions.Contains(extension))
            {
                errors.Add($"File extension '{extension}' is not allowed for security reasons");
            }
        }

        // Validate content type
        if (!IsAllowedContentType(contentType, attachmentType))
        {
            errors.Add($"File type '{contentType}' is not allowed for {attachmentType}");
        }

        // Validate file size
        if (!IsWithinSizeLimit(fileSize, attachmentType))
        {
            var maxSize = GetMaxSizeForType(attachmentType);
            errors.Add($"File size exceeds maximum allowed size of {FormatBytes(maxSize)}");
        }

        // Additional validation for images
        if (validationOptions.EnableContentVerification && 
            attachmentType == AttachmentType.Image && 
            fileStream.CanSeek)
        {
            var originalPosition = fileStream.Position;
            
            var isValidImage = await _imageProcessingService.IsValidImageAsync(fileStream, cancellationToken);
            if (!isValidImage)
            {
                errors.Add("File is not a valid image");
            }

            fileStream.Position = originalPosition; // Reset stream
        }

        // TODO: Virus scanning if enabled
        if (validationOptions.EnableVirusScanning)
        {
            _logger.LogWarning("Virus scanning is enabled but not yet implemented");
            // await ScanForVirusAsync(fileStream, cancellationToken);
        }

        var isValid = errors.Count == 0;

        if (!isValid)
        {
            _logger.LogWarning(
                "File validation failed for {FileName} ({ContentType}, {FileSize} bytes): {Errors}",
                fileName,
                contentType,
                fileSize,
                string.Join(", ", errors));
        }
        else
        {
            _logger.LogDebug(
                "File validation successful for {FileName} ({ContentType}, {FileSize} bytes)",
                fileName,
                contentType,
                fileSize);
        }

        return new FileValidationResult(isValid, errors);
    }

    public bool IsAllowedContentType(string contentType, AttachmentType attachmentType)
    {
        var validationOptions = _options.Value.Validation;
        
        var allowedTypes = attachmentType switch
        {
            AttachmentType.Image => validationOptions.AllowedImageTypes,
            AttachmentType.Document => validationOptions.AllowedDocumentTypes,
            AttachmentType.Video => validationOptions.AllowedVideoTypes,
            AttachmentType.Audio => validationOptions.AllowedAudioTypes,
            _ => Array.Empty<string>()
        };

        return allowedTypes.Contains(contentType.ToLowerInvariant());
    }

    public bool IsWithinSizeLimit(long fileSize, AttachmentType attachmentType)
    {
        var maxSize = GetMaxSizeForType(attachmentType);
        return fileSize <= maxSize;
    }

    private long GetMaxSizeForType(AttachmentType attachmentType)
    {
        var validationOptions = _options.Value.Validation;
        
        return attachmentType switch
        {
            AttachmentType.Image => validationOptions.MaxImageSizeBytes,
            AttachmentType.Document => validationOptions.MaxDocumentSizeBytes,
            AttachmentType.Video => validationOptions.MaxVideoSizeBytes,
            AttachmentType.Audio => validationOptions.MaxAudioSizeBytes,
            _ => validationOptions.MaxFileSizeBytes // Default fallback
        };
    }

    private static bool HasInvalidCharacters(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return fileName.Any(c => invalidChars.Contains(c));
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
