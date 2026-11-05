namespace Identity.Options;

/// <summary>How the Data Protection key ring is encrypted at rest (<c>identity.data_protection_keys</c>).</summary>
public enum DataProtectionKeyEncryption
{
    /// <summary>Keys are stored as plain XML. Acceptable in Development/Testing only.</summary>
    None,

    /// <summary><c>ProtectKeysWithCertificate</c>: an X.509 certificate from the store (thumbprint) or a PFX file.</summary>
    Certificate,

    /// <summary>
    /// <c>ProtectKeysWithAzureKeyVault</c>: the key ring is wrapped by an Azure Key Vault key (the key never leaves the
    /// vault). Needs <c>Identity:DataProtection:AzureKeyVault:KeyIdentifier</c>.
    /// </summary>
    AzureKeyVault,

    /// <summary>
    /// The key ring is encrypted by an AWS KMS key (custom <c>IXmlEncryptor</c>/<c>IXmlDecryptor</c> pair). Needs
    /// <c>Identity:DataProtection:AwsKms:KeyId</c>.
    /// </summary>
    AwsKms
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

    public DataProtectionAzureKeyVaultOptions AzureKeyVault { get; set; } = new();

    public DataProtectionAwsKmsOptions AwsKms { get; set; } = new();

    /// <summary>
    /// The explicit <see cref="KeyEncryption"/>, otherwise whichever provider is configured (certificate, then Azure
    /// Key Vault, then AWS KMS), otherwise <see cref="DataProtectionKeyEncryption.None"/>.
    /// </summary>
    public DataProtectionKeyEncryption EffectiveKeyEncryption =>
        KeyEncryption
        ?? (Certificate.IsConfigured ? DataProtectionKeyEncryption.Certificate
            : AzureKeyVault.IsConfigured ? DataProtectionKeyEncryption.AzureKeyVault
            : AwsKms.IsConfigured ? DataProtectionKeyEncryption.AwsKms
            : DataProtectionKeyEncryption.None);
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

/// <summary>
/// Azure Key Vault key that wraps the Data Protection keys. Authentication: client secret when
/// <see cref="TenantId"/>, <see cref="ClientId"/> and <see cref="ClientSecret"/> are all set, otherwise
/// <c>DefaultAzureCredential</c> (managed identity, workload identity, Azure CLI...). The identity needs the
/// <c>wrapKey</c> and <c>unwrapKey</c> permissions on the key.
/// </summary>
public class DataProtectionAzureKeyVaultOptions
{
    /// <summary>Full key identifier, e.g. <c>https://my-vault.vault.azure.net/keys/dataprotection</c> (a version is optional).</summary>
    public string? KeyIdentifier { get; set; }

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    /// <summary>Production reads it from a Docker secret / environment variable, never from committed files.</summary>
    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyIdentifier);
}

/// <summary>
/// AWS KMS key that encrypts the Data Protection keys. Credentials come from the default AWS chain (instance/task role,
/// environment, profile). The role needs <c>kms:Encrypt</c> and <c>kms:Decrypt</c> on the key.
/// </summary>
public class DataProtectionAwsKmsOptions
{
    /// <summary>Key id, key ARN, alias name (<c>alias/...</c>) or alias ARN.</summary>
    public string? KeyId { get; set; }

    /// <summary>AWS region system name (e.g. <c>eu-central-1</c>); null = the default region of the environment.</summary>
    public string? Region { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId);
}
