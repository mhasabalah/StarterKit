using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Attachment.Infrastructure.ImageProcessing;

public class ImageProcessingService : IImageProcessingService
{
    private readonly IOptions<AttachmentOptions> _options;
    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingService(
        IOptions<AttachmentOptions> options,
        ILogger<ImageProcessingService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<ThumbnailResult> GenerateThumbnailAsync(
        Stream imageStream,
        int? maxWidth = null,
        int? maxHeight = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var imageOptions = _options.Value.Image;
            
            // Use configured defaults if not specified
            maxWidth ??= imageOptions.ThumbnailWidth;
            maxHeight ??= imageOptions.ThumbnailHeight;

            using var image = await Image.LoadAsync(imageStream, cancellationToken);

            // Optimize image if enabled
            if (imageOptions.EnableOptimization)
            {
                image.Mutate(x => x.AutoOrient());
            }

            // Calculate resize dimensions maintaining aspect ratio
            var resizeOptions = new ResizeOptions
            {
                Size = new Size(maxWidth.Value, maxHeight.Value),
                Mode = ResizeMode.Max
            };

            image.Mutate(x => x.Resize(resizeOptions));

            var outputStream = new MemoryStream();
            var quality = imageOptions.ThumbnailQuality;
            
            await image.SaveAsJpegAsync(
                outputStream, 
                new JpegEncoder { Quality = quality }, 
                cancellationToken);
            
            outputStream.Position = 0;

            _logger.LogInformation(
                "Thumbnail generated: {Width}x{Height}, Quality={Quality}",
                image.Width,
                image.Height,
                quality);

            return new ThumbnailResult(outputStream, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating thumbnail");
            throw new InvalidOperationException("Failed to generate thumbnail", ex);
        }
    }

    public async Task<ImageDimensions> GetImageDimensionsAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var imageInfo = await Image.IdentifyAsync(imageStream, cancellationToken);
            
            if (imageInfo == null)
            {
                throw new InvalidOperationException("Unable to identify image");
            }

            return new ImageDimensions(imageInfo.Width, imageInfo.Height);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting image dimensions");
            throw;
        }
    }

    public async Task<bool> IsValidImageAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var originalPosition = imageStream.Position;
            var imageInfo = await Image.IdentifyAsync(imageStream, cancellationToken);
            imageStream.Position = originalPosition; // Reset stream
            
            return imageInfo != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Stream> OptimizeImageAsync(
        Stream imageStream,
        int? maxWidth = null,
        int? maxHeight = null,
        int? quality = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var imageOptions = _options.Value.Image;
            
            // Use configured defaults if not specified
            maxWidth ??= 1920;
            maxHeight ??= 1080;
            quality ??= imageOptions.ThumbnailQuality;

            using var image = await Image.LoadAsync(imageStream, cancellationToken);

            // Only resize if image exceeds max dimensions
            if (image.Width > maxWidth || image.Height > maxHeight)
            {
                var resizeOptions = new ResizeOptions
                {
                    Size = new Size(maxWidth.Value, maxHeight.Value),
                    Mode = ResizeMode.Max
                };

                image.Mutate(x => x.Resize(resizeOptions));
                
                _logger.LogInformation(
                    "Image resized from original to {Width}x{Height}", 
                    image.Width, 
                    image.Height);
            }

            var outputStream = new MemoryStream();
            await image.SaveAsJpegAsync(
                outputStream, 
                new JpegEncoder { Quality = quality.Value }, 
                cancellationToken);
            outputStream.Position = 0;

            _logger.LogInformation("Image optimized with quality {Quality}", quality);

            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing image");
            throw new InvalidOperationException("Failed to optimize image", ex);
        }
    }
}
