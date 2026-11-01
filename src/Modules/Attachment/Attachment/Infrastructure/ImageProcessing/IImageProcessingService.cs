namespace Attachment.Infrastructure.ImageProcessing;

public interface IImageProcessingService
{
    Task<ThumbnailResult> GenerateThumbnailAsync(
        Stream imageStream,
        int? maxWidth = null,
        int? maxHeight = null,
        CancellationToken cancellationToken = default);

    Task<ImageDimensions> GetImageDimensionsAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default);

    Task<bool> IsValidImageAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default);

    Task<Stream> OptimizeImageAsync(
        Stream imageStream,
        int? maxWidth = null,
        int? maxHeight = null,
        int? quality = null,
        CancellationToken cancellationToken = default);
}

public record ThumbnailResult(
    Stream ThumbnailStream,
    int Width,
    int Height);

public record ImageDimensions(
    int Width,
    int Height);
