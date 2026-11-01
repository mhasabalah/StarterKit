using Attachment.Data.Repository.Attachments;
using Attachment.Infrastructure.ImageProcessing;
using Attachment.Infrastructure.Storage;
using Hangfire;

namespace Attachment.Infrastructure.BackgroundJobs;

public class GenerateThumbnailJob
{
    private readonly IPendingAttachmentRepository _repository;
    private readonly IStorageService _storageService;
    private readonly IImageProcessingService _imageProcessing;
    private readonly ILogger<GenerateThumbnailJob> _logger;

    public GenerateThumbnailJob(
        IPendingAttachmentRepository repository,
        IStorageService storageService,
        IImageProcessingService imageProcessing,
        ILogger<GenerateThumbnailJob> logger)
    {
        _repository = repository;
        _storageService = storageService;
        _imageProcessing = imageProcessing;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task ExecuteAsync(Guid pendingAttachmentId)
    {
        _logger.LogInformation(
            "Starting thumbnail generation for pending attachment {PendingAttachmentId}",
            pendingAttachmentId);

        try
        {
            var pending = await _repository.GetByIdAsync(pendingAttachmentId);

            if (pending == null)
            {
                _logger.LogWarning(
                    "Pending attachment {PendingAttachmentId} not found, skipping thumbnail generation",
                    pendingAttachmentId);
                return;
            }

            if (pending.Type != Attachment.Contracts.Attachments.Dtos.AttachmentType.Image)
            {
                _logger.LogInformation(
                    "Pending attachment {PendingAttachmentId} is not an image, skipping thumbnail generation",
                    pendingAttachmentId);
                return;
            }

            // Download original image
            using var imageStream = await _storageService.DownloadAsync(pending.StoragePath);

            // Generate thumbnail
            var thumbResult = await _imageProcessing.GenerateThumbnailAsync(imageStream);

            // Upload thumbnail to temp location
            var thumbExtension = ".jpg";
            var thumbPath = $"temp/{pending.SessionId}/thumb_{pending.Id}{thumbExtension}";

            var storageResult = await _storageService.UploadToPathAsync(
                thumbResult.ThumbnailStream,
                thumbPath,
                "image/jpeg");

            // Update pending attachment with thumbnail info
            pending.SetThumbnail(
                storageResult.StoragePath,
                storageResult.PublicUrl,
                thumbResult.Width,
                thumbResult.Height);

            await _repository.SaveChangesAsync();

            _logger.LogInformation(
                "Thumbnail generated successfully for pending attachment {PendingAttachmentId}",
                pendingAttachmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error generating thumbnail for pending attachment {PendingAttachmentId}",
                pendingAttachmentId);
            throw;
        }
    }
}
