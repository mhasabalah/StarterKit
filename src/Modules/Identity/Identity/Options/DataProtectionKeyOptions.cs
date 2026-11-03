namespace Identity.Options;

/// <summary>How the Data Protection key ring is encrypted at rest (<c>identity.data_protection_keys</c>).</summary>
public enum DataProtectionKeyEncryption
{
    /// <summary>Keys are stored as plain XML. Acceptable in Development/Testing only.</summary>
    None,

    /// <summary><c>ProtectKeysWithCertificate</c>: an X.509 certificate from the store (thumbprint) or a PFX file.</summary>
    Certificate

    // Room for a KMS: add e.g. AzureKeyVault (ProtectKeysWithAzureKeyVault) or AwsKms (a custom IXmlEncryptor /
    // IXmlDecryptor pair) here and wire it in IdentityDataProtectionExtensions.ConfigureKeyEncryption.
}

/// <summary>
/// ASP.NET Data Protection settings (section <c>Identity:DataProtection</c>). The key ring encrypts authenticator
/// (TOTP) keys and signs password-reset / confirmation tokens; see wiki/Identity/Permissions-2FA-and-Admin-Audit.md.
/// </summary>
public class DataProtectionKeyOptions
{
    public const string SectionName = "Identity:DataProtection";

    /// <summary>
    /// Shared application discriminator (<c>SetApplicationName</c>). Every instance that must read the same protected
    /// payloads needs the same value. Changing it makes existing payloads (encrypted authenticator keys, outstanding
    /// reset links) unreadable - treat it as permanent.
    /// </summary>
    public string ApplicationName { get; set; } = "StarterKit";

    /// <summary>
    /// Key encryption provider. Null = <see cref="DataProtectionKeyEncryption.Certificate"/> when a certificate is
    /// configured, otherwise <see cref="DataProtectionKeyEncryption.None"/>.
    /// </summary>
    public DataProtectionKeyEncryption? KeyEncryption { get; set; }

    /// <summary>
    /// Outside Development/Testing: true = refuse to start with unencrypted keys; false (default) = start and log a
    /// warning at startup. Default false so an upgrade or a fresh deploy without a provisioned certificate keeps
    /// working; set true once the certificate is in place (docker-compose.dataprotection.yml does).
    /// </summary>
    public bool RequireKeyEncryption { get; set; }

    public DataProtectionCertificateOptions Certificate { get; set; } = new();

    public DataProtectionKeyEncryption EffectiveKeyEncryption =>
        KeyEncryption ?? (Certificate.IsConfigured ? DataProtectionKeyEncryption.Certificate : DataProtectionKeyEncryption.None);
}

/// <summary>The key-encryption certificate: <see cref="Thumbprint"/> (certificate store) OR <see cref="Path"/> (PFX).</summary>
public class DataProtectionCertificateOptions
{
    /// <summary>Thumbprint of a certificate (with private key) in <see cref="StoreName"/>/<see cref="StoreLocation"/>.</summary>
    public string? Thumbprint { get; set; }

    public string StoreName { get; set; } = "My";

    public string StoreLocation { get; set; } = "CurrentUser";

    /// <summary>PFX file path, e.g. the Docker secret <c>/run/secrets/dataprotection_cert.pfx</c>.</summary>
    public string? Path { get; set; }

    /// <summary>PFX password; production reads it from the <c>dataprotection_cert_password</c> Docker secret.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Old PFX files still needed to DECRYPT keys written before a certificate rotation (never used to encrypt).
    /// Same <see cref="Password"/>. Keep each until every key it protected has expired (default key lifetime 90 days).
    /// </summary>
    public string[]? PreviousPaths { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Thumbprint) || !string.IsNullOrWhiteSpace(Path);
}
