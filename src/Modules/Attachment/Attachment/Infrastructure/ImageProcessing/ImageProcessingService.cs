using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Attachment.Infrastructure.ImageProcessing;

public interface IImageProcessingService
{
    /// <summary>
    /// Creates a JPEG thumbnail. The image is decoded directly at (about) the target size and only the first frame
    /// is read, so large images and animated GIFs never get fully decoded into memory. Callers must validate the
    /// megapixels first (<see cref="IdentifyAsync"/>).
    /// </summary>
    Task<ThumbnailResult> GenerateThumbnailAsync(Stream imageStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads only the image header (no pixel decode): detected MIME type and dimensions, or <c>null</c> when the
    /// content is not an image this processor understands.
    /// </summary>
    Task<ImageIdentity?> IdentifyAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

public record ThumbnailResult(Stream ThumbnailStream, int Width, int Height);

public record ImageIdentity(string? MimeType, int Width, int Height);

// The ONLY file that references SixLabors.ImageSharp (Six Labors Split License: free below USD 1M annual revenue or
// for open source, commercial otherwise). Swapping the library means a new IImageProcessingService adapter;
// ThirdPartyIsolationTests keeps the package inside the Attachment module.

public sealed class ImageProcessingService(
    IOptions<AttachmentOptions> options,
    ILogger<ImageProcessingService> logger) : IImageProcessingService
{
    public async Task<ImageIdentity?> IdentifyAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await Image.IdentifyAsync(imageStream, cancellationToken);
            return new ImageIdentity(info.Metadata.DecodedImageFormat?.DefaultMimeType, info.Width, info.Height);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<ThumbnailResult> GenerateThumbnailAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        var imageOptions = options.Value.Image;
        var target = new Size(imageOptions.ThumbnailWidth, imageOptions.ThumbnailHeight);

        var decoderOptions = new DecoderOptions
        {
            TargetSize = target,
            MaxFrames = 1
        };

        using var image = await Image.LoadAsync(decoderOptions, imageStream, cancellationToken);

        if (imageOptions.EnableOptimization)
            image.Mutate(x => x.AutoOrient());

        image.Mutate(x => x.Resize(new ResizeOptions { Size = target, Mode = ResizeMode.Max }));
        image.Metadata.ExifProfile = null; // never copy EXIF (GPS, device) into the thumbnail

        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = imageOptions.ThumbnailQuality }, cancellationToken);
        output.Position = 0;

        logger.LogDebug("Thumbnail generated: {Width}x{Height}", image.Width, image.Height);
        return new ThumbnailResult(output, image.Width, image.Height);
    }
}
