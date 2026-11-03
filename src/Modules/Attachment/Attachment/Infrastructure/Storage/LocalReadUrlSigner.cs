using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

/// <summary>
/// Signs and verifies the self-authenticating read URLs of Local storage
/// (<c>GET /api/v1/attachments/files?p=&amp;e=&amp;n=&amp;t=&amp;i=&amp;s=</c>). The signature covers the
/// path, expiry and the response headers, so none of them can be changed by the client.
/// </summary>
public interface ILocalReadUrlSigner
{
    string CreateUrl(string storagePath, DateTimeOffset expiresAt, ReadUrlOptions? options);

    /// <summary>Returns the verified request, or null when the signature is invalid or expired.</summary>
    LocalReadUrlRequest? Verify(string? path, string? expires, string? fileName, string? contentType,
        string? inline, string? signature, DateTimeOffset now);
}

public sealed record LocalReadUrlRequest(string StoragePath, string? FileName, string? ContentType, bool Inline);

public sealed class LocalReadUrlSigner(IAttachmentSigningKeys keys, IOptions<AttachmentOptions> options)
    : ILocalReadUrlSigner
{
    public const string RoutePath = "/api/v1/attachments/files";
    private const string Purpose = "local-read-url-v1";

    public string CreateUrl(string storagePath, DateTimeOffset expiresAt, ReadUrlOptions? readOptions)
    {
        var exp = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var name = readOptions?.FileName ?? string.Empty;
        var type = readOptions?.ContentType ?? string.Empty;
        var inline = readOptions?.Inline == true ? "1" : "0";
        var sig = Sign(storagePath, exp, name, type, inline);

        var baseUrl = options.Value.Download.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;
        return $"{baseUrl}{RoutePath}?p={Uri.EscapeDataString(storagePath)}&e={exp}" +
               $"&n={Uri.EscapeDataString(name)}&t={Uri.EscapeDataString(type)}&i={inline}&s={sig}";
    }

    public LocalReadUrlRequest? Verify(string? path, string? expires, string? fileName, string? contentType,
        string? inline, string? signature, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(expires) || string.IsNullOrEmpty(signature) ||
            inline is not ("0" or "1") ||
            !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var exp))
        {
            return null;
        }

        if (now.ToUnixTimeSeconds() > exp)
            return null;

        var expected = Encoding.ASCII.GetBytes(Sign(path, expires, fileName ?? "", contentType ?? "", inline));
        var actual = Encoding.ASCII.GetBytes(signature);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return null;

        return new LocalReadUrlRequest(
            path,
            string.IsNullOrEmpty(fileName) ? null : fileName,
            string.IsNullOrEmpty(contentType) ? null : contentType,
            inline == "1");
    }

    private string Sign(string path, string exp, string name, string type, string inline)
    {
        var payload = Encoding.UTF8.GetBytes($"{path}\n{exp}\n{name}\n{type}\n{inline}");
        var mac = HMACSHA256.HashData(keys.GetKey(Purpose), payload);
        return WebEncoders.Base64UrlEncode(mac);
    }
}
