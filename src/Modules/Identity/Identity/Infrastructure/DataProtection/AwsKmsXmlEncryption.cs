using System.Text;
using System.Xml.Linq;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Identity.Options;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.DataProtection;

/// <summary>
/// Encrypts Data Protection key elements with an AWS KMS key. The encrypted element stores the KMS key id and the
/// ciphertext; the matching <see cref="AwsKmsXmlDecryptor"/> (created by Data Protection through DI) calls KMS to read
/// it back. The key material of the ring never leaves the process unencrypted and the KMS key never leaves AWS.
/// <para>
/// KMS <c>Encrypt</c> accepts at most 4 KB of plaintext; a Data Protection key element is ~1 KB, so no envelope step
/// is needed. The encryption context binds the ciphertext to this application (an attacker who copies the table
/// cannot decrypt it for another application name).
/// </para>
/// </summary>
public sealed class AwsKmsXmlEncryptor(IAmazonKeyManagementService kms, string keyId, string applicationName)
    : IXmlEncryptor
{
    public const string ElementName = "awsKmsEncryptedKey";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        try
        {
            using var stream = new MemoryStream(plaintext);
            var response = kms.EncryptAsync(new EncryptRequest
            {
                KeyId = keyId,
                Plaintext = stream,
                EncryptionContext = AwsKmsEncryptionContext.For(applicationName)
            }).GetAwaiter().GetResult();

            var element = new XElement(ElementName,
                new XAttribute("keyId", response.KeyId ?? keyId),
                new XElement("ciphertext", Convert.ToBase64String(response.CiphertextBlob.ToArray())));

            return new EncryptedXmlInfo(element, typeof(AwsKmsXmlDecryptor));
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }
}

/// <summary>Reads elements written by <see cref="AwsKmsXmlEncryptor"/>. Instantiated by Data Protection via DI.</summary>
public sealed class AwsKmsXmlDecryptor : IXmlDecryptor
{
    private readonly IAmazonKeyManagementService _kms;
    private readonly string _applicationName;

    public AwsKmsXmlDecryptor(IServiceProvider services)
    {
        _kms = (IAmazonKeyManagementService?)services.GetService(typeof(IAmazonKeyManagementService))
               ?? throw new InvalidOperationException(
                   "IAmazonKeyManagementService is not registered; AWS KMS key encryption is not configured.");
        _applicationName = ((IOptions<DataProtectionKeyOptions>?)services.GetService(typeof(IOptions<DataProtectionKeyOptions>)))
            ?.Value.ApplicationName ?? "StarterKit";
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var ciphertext = Convert.FromBase64String(
            (string?)encryptedElement.Element("ciphertext")
            ?? throw new InvalidOperationException("The AWS KMS encrypted key element has no ciphertext."));

        using var stream = new MemoryStream(ciphertext);
        var response = _kms.DecryptAsync(new DecryptRequest
        {
            CiphertextBlob = stream,
            // Passing the stored key id pins decryption to the key that encrypted it.
            KeyId = (string?)encryptedElement.Attribute("keyId"),
            EncryptionContext = AwsKmsEncryptionContext.For(_applicationName)
        }).GetAwaiter().GetResult();

        var plaintext = response.Plaintext.ToArray();
        try
        {
            return XElement.Parse(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }
}

internal static class AwsKmsEncryptionContext
{
    public static Dictionary<string, string> For(string applicationName) => new()
    {
        ["purpose"] = "aspnet-dataprotection-keyring",
        ["application"] = applicationName
    };
}
