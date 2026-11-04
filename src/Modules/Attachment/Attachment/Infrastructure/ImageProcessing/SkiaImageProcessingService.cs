using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Attachment.Infrastructure.ImageProcessing;

/// <summary>
/// Default <see cref="IImageProcessingService"/> adapter on SkiaSharp (MIT; no license key, no revenue limit).
/// The ONLY production file that references SkiaSharp (guarded by <c>ThirdPartyIsolationTests</c>).
/// </summary>
public sealed class SkiaImageProcessingService(
    IOptions<AttachmentOptions> options,
    ILogger<SkiaImageProcessingService> logger) : IImageProcessingService
{
    public async Task<ImageIdentity?> IdentifyAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        var source = await SeekableAsync(imageStream, cancellationToken);
        try
        {
            using var codec = OpenCodec(source);
            return codec is null
                ? null
                : new ImageIdentity(MimeTypeOf(codec.EncodedFormat), codec.Info.Width, codec.Info.Height);
        }
        finally
        {
            if (!ReferenceEquals(source, imageStream))
                await source.DisposeAsync();
        }
    }

    public async Task<ThumbnailResult> GenerateThumbnailAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value.Image;
        var source = await SeekableAsync(imageStream, cancellationToken);
        try
        {
            using var codec = OpenCodec(source)
                              ?? throw new InvalidOperationException("The content is not a supported image.");
            cancellationToken.ThrowIfCancellationRequested();

            var origin = settings.EnableOptimization ? codec.EncodedOrigin : SKEncodedOrigin.TopLeft;
            var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

            // Final size: fit inside the box, aspect ratio kept, in upright dimensions.
            var (uprightWidth, uprightHeight) = swapsAxes
                ? (codec.Info.Height, codec.Info.Width)
                : (codec.Info.Width, codec.Info.Height);
            var ratio = Math.Min((float)settings.ThumbnailWidth / uprightWidth,
                (float)settings.ThumbnailHeight / uprightHeight);
            var width = Math.Max(1, (int)Math.Round(uprightWidth * ratio));
            var height = Math.Max(1, (int)Math.Round(uprightHeight * ratio));

            using var decoded = Decode(codec, Math.Min(1f, ratio));

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
                                ?? throw new InvalidOperationException("Could not allocate the thumbnail surface.");
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White); // JPEG has no alpha: transparent areas become white, not black

            var (decodedUprightWidth, decodedUprightHeight) = swapsAxes
                ? (decoded.Height, decoded.Width)
                : (decoded.Width, decoded.Height);
            canvas.Scale((float)width / decodedUprightWidth, (float)height / decodedUprightHeight);
            canvas.Concat(OrientationMatrix(origin, decoded.Width, decoded.Height));

            using (var image = SKImage.FromBitmap(decoded))
            {
                canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }

            canvas.Flush();
            using var snapshot = surface.Snapshot();
            using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, settings.ThumbnailQuality)
                             ?? throw new InvalidOperationException("JPEG encoding failed.");

            logger.LogDebug("Thumbnail generated: {Width}x{Height}", width, height);
            return new ThumbnailResult(new MemoryStream(jpeg.ToArray(), writable: false), width, height);
        }
        finally
        {
            if (!ReferenceEquals(source, imageStream))
                await source.DisposeAsync();
        }
    }

    /// <summary>
    /// First frame, decoded at the smallest size the codec offers that is still at least <paramref name="scale"/>
    /// (JPEG decodes natively at 1/2, 1/4, 1/8; others decode at full size), so a large photo is never fully decoded
    /// just to make a 200px thumbnail.
    /// </summary>
    private static SKBitmap Decode(SKCodec codec, float scale)
    {
        var scaled = scale < 1f ? codec.GetScaledDimensions(scale) : codec.Info.Size;
        var bitmap = TryDecode(codec, scaled);
        if (bitmap is null && scaled != codec.Info.Size)
            bitmap = TryDecode(codec, codec.Info.Size);
        return bitmap ?? throw new InvalidOperationException("The image could not be decoded.");
    }

    private static SKBitmap? TryDecode(SKCodec codec, SKSizeI size)
    {
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        return null;
    }

    /// <summary>Maps decoded-bitmap coordinates (w x h) to upright coordinates for an EXIF orientation.</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),     // mirror horizontally
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1), // rotate 180
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // mirror vertically
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),       // transpose
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),     // rotate 90 clockwise
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1), // transverse
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),   // rotate 90 counter-clockwise
        _ => SKMatrix.Identity
    };

    /// <summary>The wrapper never owns the caller's stream, which stays open (validation re-reads it).</summary>
    private static SKCodec? OpenCodec(Stream stream)
    {
        var skStream = new SKManagedStream(stream, disposeManagedStream: false);
        var codec = SKCodec.Create(skStream, out _);
        if (codec is null)
            skStream.Dispose(); // otherwise the codec owns (and disposes) the wrapper
        return codec;
    }

    /// <summary>Codecs seek; provider download streams may not. Only those are copied (sizes are capped by validation).</summary>
    private static async Task<Stream> SeekableAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            return stream;

        var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }

    private static string? MimeTypeOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Png => "image/png",
        SKEncodedImageFormat.Gif => "image/gif",
        SKEncodedImageFormat.Webp => "image/webp",
        SKEncodedImageFormat.Bmp => "image/bmp",
        _ => null // ico, wbmp, heif, avif, dng...: not accepted uploads
    };
}
