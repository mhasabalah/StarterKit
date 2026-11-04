using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.ImageProcessing;
using Attachment.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Attachment.Tests.Unit;

/// <summary>
/// The behavior every <see cref="IImageProcessingService"/> adapter must have. Switching the imaging library
/// (licensed ImageSharp, Magick.NET, ...) = a new subclass of this class that returns the new adapter; it must pass
/// unchanged. Pixels are inspected with SkiaSharp only as a test tool.
/// </summary>
public abstract class ImageProcessingContractTests
{
    protected abstract IImageProcessingService Create(AttachmentOptions options);

    private IImageProcessingService Service(int box = 200) =>
        Create(new AttachmentOptions { Image = { ThumbnailWidth = box, ThumbnailHeight = box, ThumbnailQuality = 85 } });

    public static TheoryData<string, string> Formats => new()
    {
        { "png", "image/png" }, { "jpeg", "image/jpeg" }, { "webp", "image/webp" }, { "gif", "image/gif" }, { "bmp", "image/bmp" }
    };

    private static (byte[] Bytes, int Width, int Height) Sample(string format) => format switch
    {
        "png" => (TestFiles.Png(40, 30), 40, 30),
        "jpeg" => (TestFiles.Jpeg(40, 30), 40, 30),
        "webp" => (TestFiles.Webp(40, 30), 40, 30),
        "gif" => (TestFiles.Gif(), 1, 1),
        _ => (TestFiles.Bmp(4, 3), 4, 3)
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Identify_ReportsTheRealFormatAndSize(string format, string mime)
    {
        var (bytes, width, height) = Sample(format);

        var identity = await Service().IdentifyAsync(new MemoryStream(bytes));

        identity.Should().Be(new ImageIdentity(mime, width, height));
    }

    [Fact]
    public async Task Identify_NonImage_ReturnsNull()
    {
        (await Service().IdentifyAsync(new MemoryStream(TestFiles.Pdf()))).Should().BeNull();
        (await Service().IdentifyAsync(new MemoryStream([]))).Should().BeNull();
    }

    [Fact]
    public async Task Identify_ReadsTheHeaderOnly_SoADecompressionBombIsMeasuredNotDecoded()
    {
        var identity = await Service().IdentifyAsync(new MemoryStream(TestFiles.PngBombHeader(50_000, 50_000)));

        identity.Should().Be(new ImageIdentity("image/png", 50_000, 50_000), "the validator rejects it by megapixels");
    }

    [Fact]
    public async Task Identify_LeavesTheCallersStreamOpen()
    {
        var stream = new MemoryStream(TestFiles.Png());

        await Service().IdentifyAsync(stream);

        stream.CanRead.Should().BeTrue("validation rewinds and re-reads it (malware scan)");
    }

    [Theory]
    [InlineData(400, 200, 200, 100)]
    [InlineData(200, 400, 100, 200)]
    [InlineData(300, 300, 200, 200)]
    public async Task Thumbnail_FitsTheBox_KeepsTheAspectRatio_AndIsAJpeg(int w, int h, int expectedW, int expectedH)
    {
        var result = await Service().GenerateThumbnailAsync(new MemoryStream(TestFiles.Jpeg(w, h)));

        (result.Width, result.Height).Should().Be((expectedW, expectedH));
        var output = await Service().IdentifyAsync(result.ThumbnailStream);
        output.Should().Be(new ImageIdentity("image/jpeg", expectedW, expectedH));
    }

    [Fact]
    public async Task Thumbnail_FromANonSeekableStream_Works()
    {
        var result = await Service().GenerateThumbnailAsync(new NonSeekableStream(TestFiles.Png(400, 200)));

        (result.Width, result.Height).Should().Be((200, 100));
    }

    [Fact]
    public async Task Thumbnail_OfATransparentImage_IsWhiteNotBlack()
    {
        var result = await Service().GenerateThumbnailAsync(new MemoryStream(TestFiles.TransparentPng(40, 40)));

        var pixel = PixelAt(result, 0.5f, 0.5f);
        ((int)pixel.Red).Should().BeGreaterThan(240);
        ((int)pixel.Green).Should().BeGreaterThan(240);
        ((int)pixel.Blue).Should().BeGreaterThan(240);
    }

    /// <summary>
    /// Phones store photos sideways plus an EXIF orientation. Source: 400x200, left half RED, right half BLUE.
    /// </summary>
    [Theory]
    [InlineData(1, 200, 100, "left")]   // as stored
    [InlineData(3, 200, 100, "right")]  // rotate 180: red ends up on the right
    [InlineData(6, 100, 200, "top")]    // rotate 90 clockwise: left edge becomes the top
    [InlineData(8, 100, 200, "bottom")] // rotate 90 counter-clockwise: left edge becomes the bottom
    public async Task Thumbnail_AppliesTheExifOrientation(int orientation, int expectedW, int expectedH, string redSide)
    {
        var jpeg = WithExifOrientation(TwoColorJpeg(400, 200), orientation);

        var result = await Service().GenerateThumbnailAsync(new MemoryStream(jpeg));

        (result.Width, result.Height).Should().Be((expectedW, expectedH));
        var (rx, ry, bx, by) = redSide switch
        {
            "left" => (0.2f, 0.5f, 0.8f, 0.5f),
            "right" => (0.8f, 0.5f, 0.2f, 0.5f),
            "top" => (0.5f, 0.2f, 0.5f, 0.8f),
            _ => (0.5f, 0.8f, 0.5f, 0.2f)
        };
        IsRed(PixelAt(result, rx, ry)).Should().BeTrue($"red belongs on the {redSide}");
        IsBlue(PixelAt(result, bx, by)).Should().BeTrue("blue is on the opposite side");
    }

    private static bool IsRed(SKColor c) => c.Red > 180 && c.Blue < 80;

    private static bool IsBlue(SKColor c) => c.Blue > 180 && c.Red < 80;

    private static SKColor PixelAt(ThumbnailResult result, float fx, float fy)
    {
        // SKBitmap.Decode(Stream) would dispose the stream: decode a copy of the bytes instead.
        result.ThumbnailStream.Position = 0;
        using var copy = new MemoryStream();
        result.ThumbnailStream.CopyTo(copy);
        result.ThumbnailStream.Position = 0;
        using var bitmap = SKBitmap.Decode(copy.ToArray());
        return bitmap.GetPixel((int)(bitmap.Width * fx), (int)(bitmap.Height * fy));
    }

    private static byte[] TwoColorJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 2f, height, red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    /// <summary>Inserts a minimal EXIF APP1 segment (one IFD entry: Orientation) right after the JPEG SOI marker.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, int orientation)
    {
        byte[] tiff =
        [
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // little-endian TIFF header, IFD at offset 8
            0x01, 0x00,                                                 // 1 entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,             // tag 0x0112 Orientation, SHORT, count 1
            (byte)orientation, 0x00, 0x00, 0x00,                        // value
            0x00, 0x00, 0x00, 0x00                                      // no next IFD
        ];
        byte[] exifHeader = [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0];
        var length = 2 + exifHeader.Length + tiff.Length;

        var output = new List<byte>(jpeg.Length + length + 2) { 0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)length };
        output.AddRange(exifHeader);
        output.AddRange(tiff);
        output.AddRange(jpeg.Skip(2));
        return output.ToArray();
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

public sealed class SkiaImageProcessingContractTests : ImageProcessingContractTests
{
    protected override IImageProcessingService Create(AttachmentOptions options) =>
        new SkiaImageProcessingService(TestOptions.Wrap(options), NullLogger<SkiaImageProcessingService>.Instance);
}
