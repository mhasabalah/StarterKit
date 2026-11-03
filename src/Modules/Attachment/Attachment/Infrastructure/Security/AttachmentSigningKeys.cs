using System.Security.Cryptography;
using System.Text;
using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Security;

/// <summary>
/// HMAC keys for the Attachment module (pending session ids, Local signed read URLs). One root secret,
/// one derived key per purpose (HKDF), so a value signed for one purpose is never valid for another.
/// </summary>
public interface IAttachmentSigningKeys
{
    byte[] GetKey(string purpose);
}

public sealed class AttachmentSigningKeys : IAttachmentSigningKeys
{
    private const string JwtSecretConfigKey = "JwtSettings:SecretKey";
    private readonly byte[] _rootKey;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public AttachmentSigningKeys(
        IOptions<AttachmentOptions> options,
        IConfiguration configuration,
        ILogger<AttachmentSigningKeys> logger)
    {
        var configured = options.Value.Download.SigningKey;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _rootKey = Encoding.UTF8.GetBytes(configured);
            return;
        }

        var jwtSecret = configuration[JwtSecretConfigKey];
        if (!string.IsNullOrWhiteSpace(jwtSecret))
        {
            _rootKey = Encoding.UTF8.GetBytes(jwtSecret);
            return;
        }

        logger.LogWarning(
            "No Attachment:Download:SigningKey or {JwtKey} configured. Using a random per-process key: pending " +
            "upload sessions and Local signed URLs will not survive a restart or work across instances.",
            JwtSecretConfigKey);
        _rootKey = RandomNumberGenerator.GetBytes(32);
    }

    /// <summary>Test/explicit constructor.</summary>
    public AttachmentSigningKeys(byte[] rootKey) => _rootKey = rootKey;

    public byte[] GetKey(string purpose)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(purpose, out var key))
            {
                key = HKDF.DeriveKey(HashAlgorithmName.SHA256, _rootKey, 32,
                    salt: Encoding.UTF8.GetBytes("StarterKit.Attachment"),
                    info: Encoding.UTF8.GetBytes(purpose));
                _cache[purpose] = key;
            }

            return key;
        }
    }
}
