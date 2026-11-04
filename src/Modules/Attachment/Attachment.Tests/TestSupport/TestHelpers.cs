using System.Buffers.Binary;
using System.Text;
using Attachment.Contracts.Authorization;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Security;
using Attachment.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Core.Web;
using SkiaSharp;
using AttachmentEntityType = Attachment.Contracts.Attachments.Dtos.EntityType;

namespace Attachment.Tests.TestSupport;

/// <summary>Who is calling: user id (null = anonymous), roles, and client fingerprint inputs.</summary>
public sealed record Caller(Guid? UserId, string[] Roles, string Ip = "10.0.0.1", string UserAgent = "test-agent")
{
    public static Caller Anonymous(string ip = "10.0.0.1", string ua = "test-agent") => new(null, [], ip, ua);
    public static Caller User(Guid id) => new(id, ["User"]);
    public static Caller Admin(Guid? id = null) => new(id ?? Guid.NewGuid(), ["Admin"]);

    public IHttpContextService ToHttpContext()
    {
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserIdAsGuid()).Returns(UserId);
        http.Setup(h => h.GetUserId()).Returns(UserId?.ToString());
        http.Setup(h => h.IsAuthenticated()).Returns(UserId.HasValue);
        http.Setup(h => h.GetRoles()).Returns(Roles);
        http.Setup(h => h.GetIpAddress()).Returns(Ip);
        http.Setup(h => h.GetUserAgent()).Returns(UserAgent);
        return http.Object;
    }
}

public static class TestOptions
{
    public static AttachmentOptions Create(Action<AttachmentOptions>? configure = null)
    {
        var options = new AttachmentOptions().ApplyDefaults();
        configure?.Invoke(options);
        return options;
    }

    public static IOptions<AttachmentOptions> Wrap(AttachmentOptions options) => Options.Create(options);
}

public static class TestServices
{
    public static readonly IAttachmentSigningKeys SigningKeys =
        new AttachmentSigningKeys(Encoding.UTF8.GetBytes("unit-test-signing-key-0123456789abcdef"));

    public static PendingSessionService Sessions(Caller caller) => new(SigningKeys, caller.ToHttpContext());

    /// <summary>The real access service with the built-in User policy (plus optional extra registrations).</summary>
    public static AttachmentAccessService Access(Caller caller, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddAttachmentEntityPolicy<UserAttachmentEntityAccessPolicy>(AttachmentEntityType.User);
        register?.Invoke(services);
        var provider = services.BuildServiceProvider().CreateScope().ServiceProvider;

        return new AttachmentAccessService(provider, caller.ToHttpContext(), NullLogger<AttachmentAccessService>.Instance);
    }

    public static LocalFileStorageService LocalStorage(string root, AttachmentOptions? options = null)
    {
        options ??= TestOptions.Create();
        options.Storage.Local.UploadPath = root;
        var wrapped = TestOptions.Wrap(options);

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        return new LocalFileStorageService(env.Object, wrapped, new LocalReadUrlSigner(SigningKeys, wrapped),
            NullLogger<LocalFileStorageService>.Instance);
    }
}

/// <summary>A temp folder deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "attachment-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string[] AllFiles() => Directory.GetFiles(Path, "*", SearchOption.AllDirectories);

    public void Dispose()
    {
        try
        { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class TestFiles
{
    public static byte[] Png(int width = 32, int height = 24) =>
        Encode(width, height, new SKColor(10, 120, 200), SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width = 32, int height = 24) =>
        Encode(width, height, new SKColor(200, 10, 10), SKEncodedImageFormat.Jpeg);

    public static byte[] Webp(int width = 32, int height = 24) =>
        Encode(width, height, new SKColor(10, 200, 10), SKEncodedImageFormat.Webp);

    /// <summary>A fully transparent PNG (thumbnails must flatten it onto white, not black).</summary>
    public static byte[] TransparentPng(int width = 32, int height = 24) =>
        Encode(width, height, SKColors.Transparent, SKEncodedImageFormat.Png);

    /// <summary>A 1x1 GIF89a (Skia has no GIF encoder).</summary>
    public static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");

    /// <summary>An uncompressed 24-bit BMP of the given size (Skia has no BMP encoder).</summary>
    public static byte[] Bmp(int width = 4, int height = 3)
    {
        var rowSize = (width * 3 + 3) & ~3;
        var bytes = new byte[54 + rowSize * height];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        return bytes;
    }

    private static byte[] Encode(int width, int height, SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// A tiny, structurally valid PNG whose header claims <paramref name="width"/> x <paramref name="height"/>
    /// pixels (decompression bomb). Identify reads only the header, so no pixels are ever allocated.
    /// </summary>
    public static byte[] PngBombHeader(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // RGB
        WriteChunk(ms, "IHDR", ihdr);
        WriteChunk(ms, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32([.. typeBytes, .. data]);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        s.Write(crcBytes);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}
