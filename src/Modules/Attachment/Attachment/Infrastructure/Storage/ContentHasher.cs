using System.Security.Cryptography;

namespace Attachment.Infrastructure.Storage;

/// <summary>SHA-256 of uploaded content, stored on Ready rows (basis for duplicate detection).</summary>
public static class ContentHasher
{
    /// <summary>
    /// Lowercase hex SHA-256 of a seekable stream, read from the start. The position is reset to 0 afterwards so
    /// the caller can upload the same stream.
    /// </summary>
    public static async Task<string> Sha256HexAsync(Stream seekableStream, CancellationToken cancellationToken)
    {
        if (!seekableStream.CanSeek)
            throw new ArgumentException("The stream must be seekable.", nameof(seekableStream));

        seekableStream.Position = 0;
        var hash = await SHA256.HashDataAsync(seekableStream, cancellationToken);
        seekableStream.Position = 0;
        return Convert.ToHexStringLower(hash);
    }
}
