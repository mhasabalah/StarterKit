namespace Attachment.Infrastructure.ImageProcessing;

/// <summary>
/// The image-processing PORT of the Attachment module. Nothing else in the codebase touches an imaging library: the
/// adapter is chosen by ONE registration line in <c>AttachmentModule</c>. Default: <see cref="SkiaImageProcessingService"/>
/// (SkiaSharp, MIT). To switch (licensed ImageSharp, Magick.NET, a cloud service...), add one class implementing this
/// interface, change that registration, and point <c>Attachment.Tests/Unit/ImageProcessingContractTests</c> at it.
/// See wiki/Attachment/Image-Processing.md.
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Creates a JPEG thumbnail that fits inside <c>Attachment:Image:ThumbnailWidth</c> x <c>ThumbnailHeight</c>
    /// (aspect ratio kept), from the FIRST frame only, upright (EXIF orientation applied when
    /// <c>EnableOptimization</c>), without metadata (no EXIF/GPS), transparency flattened onto white.
    /// Callers must validate the megapixels first (<see cref="IdentifyAsync"/>).
    /// </summary>
    Task<ThumbnailResult> GenerateThumbnailAsync(Stream imageStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads only the image header (no pixel decode): detected MIME type and dimensions, or <c>null</c> when the
    /// content is not an image this processor understands. Never disposes <paramref name="imageStream"/>.
    /// </summary>
    Task<ImageIdentity?> IdentifyAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

public record ThumbnailResult(Stream ThumbnailStream, int Width, int Height);

public record ImageIdentity(string? MimeType, int Width, int Height);
