using System.Security.Cryptography.X509Certificates;
using Amazon;
using Amazon.KeyManagementService;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Identity.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.DataProtection;

/// <summary>
/// Data Protection for the Identity module: shared application name, key ring in the database
/// (<see cref="DataProtectionKeyRepository"/>), optional at-rest key encryption and the production guard.
/// Settings: <see cref="DataProtectionKeyOptions"/> (<c>Identity:DataProtection</c>).
/// </summary>
public static class IdentityDataProtectionExtensions
{
    public static IServiceCollection AddIdentityDataProtection(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DataProtectionKeyOptions.SectionName);
        services.Configure<DataProtectionKeyOptions>(section);
        // Read eagerly: the certificate is loaded at registration, so a missing/unreadable PFX fails startup.
        var options = section.Get<DataProtectionKeyOptions>() ?? new DataProtectionKeyOptions();

        var builder = services.AddDataProtection().SetApplicationName(options.ApplicationName);

        // The key ring lives in the database so every instance shares it and it survives redeploys.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(o =>
                o.XmlRepository = new DataProtectionKeyRepository(sp.GetRequiredService<IServiceScopeFactory>())));

        ConfigureKeyEncryption(builder, options);

        services.AddHostedService<DataProtectionKeyEncryptionCheck>();
        return services;
    }

    /// <summary>Applies <see cref="DataProtectionKeyOptions.EffectiveKeyEncryption"/> to the builder.</summary>
    public static void ConfigureKeyEncryption(IDataProtectionBuilder builder, DataProtectionKeyOptions options)
    {
        switch (options.EffectiveKeyEncryption)
        {
            case DataProtectionKeyEncryption.None:
                return;

            case DataProtectionKeyEncryption.Certificate:
                var current = LoadCertificate(options.Certificate);
                var previous = (options.Certificate.PreviousPaths ?? [])
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => LoadPfx(p, options.Certificate.Password));

                builder.ProtectKeysWithCertificate(current);
                // Decryption looks certificates up in the store by default; a PFX-loaded certificate (and rotated-out
                // ones) must be registered explicitly or the app cannot read its own keys back.
                builder.UnprotectKeysWithAnyCertificate([current, .. previous]);
                return;

            case DataProtectionKeyEncryption.AzureKeyVault:
                // Wrap/unwrap happens inside the vault; the app only needs wrapKey/unwrapKey rights on the key.
                builder.ProtectKeysWithAzureKeyVault(
                    ParseKeyIdentifier(options.AzureKeyVault), CreateAzureCredential(options.AzureKeyVault));
                return;

            case DataProtectionKeyEncryption.AwsKms:
                ConfigureAwsKms(builder, options);
                return;

            default:
                throw new NotSupportedException(
                    $"{DataProtectionKeyOptions.SectionName}:KeyEncryption '{options.EffectiveKeyEncryption}' is not supported.");
        }
    }

    /// <summary>The vault key URI (https://&lt;vault&gt;.vault.azure.net/keys/&lt;name&gt;[/&lt;version&gt;]).</summary>
    public static Uri ParseKeyIdentifier(DataProtectionAzureKeyVaultOptions options)
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{DataProtectionKeyOptions.SectionName}:KeyEncryption is AzureKeyVault but AzureKeyVault:KeyIdentifier is not set.");
        }

        if (!Uri.TryCreate(options.KeyIdentifier, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{DataProtectionKeyOptions.SectionName}:AzureKeyVault:KeyIdentifier must be an absolute https URI " +
                "(https://<vault>.vault.azure.net/keys/<name>).");
        }

        return uri;
    }

    /// <summary>Client-secret credential when fully configured, otherwise <see cref="DefaultAzureCredential"/> (managed identity...).</summary>
    public static TokenCredential CreateAzureCredential(DataProtectionAzureKeyVaultOptions options) =>
        !string.IsNullOrWhiteSpace(options.TenantId) && !string.IsNullOrWhiteSpace(options.ClientId) &&
        !string.IsNullOrWhiteSpace(options.ClientSecret)
            ? new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret)
            : new DefaultAzureCredential();

    private static void ConfigureAwsKms(IDataProtectionBuilder builder, DataProtectionKeyOptions options)
    {
        if (!options.AwsKms.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{DataProtectionKeyOptions.SectionName}:KeyEncryption is AwsKms but AwsKms:KeyId is not set.");
        }

        var keyId = options.AwsKms.KeyId!;
        var region = options.AwsKms.Region;

        // TryAdd: tests (or a host with its own AWS configuration) can register the client first.
        builder.Services.TryAddSingleton<IAmazonKeyManagementService>(_ =>
            string.IsNullOrWhiteSpace(region)
                ? new AmazonKeyManagementServiceClient()
                : new AmazonKeyManagementServiceClient(RegionEndpoint.GetBySystemName(region)));

        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(o => o.XmlEncryptor = new AwsKmsXmlEncryptor(
                sp.GetRequiredService<IAmazonKeyManagementService>(), keyId, options.ApplicationName)));
    }

    /// <summary>The configured certificate (store thumbprint wins over PFX path). It must carry a private key.</summary>
    public static X509Certificate2 LoadCertificate(DataProtectionCertificateOptions options)
    {
        X509Certificate2 certificate;
        if (!string.IsNullOrWhiteSpace(options.Thumbprint))
        {
            using var store = new X509Store(
                Enum.Parse<StoreName>(options.StoreName, ignoreCase: true),
                Enum.Parse<StoreLocation>(options.StoreLocation, ignoreCase: true));
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var thumbprint = options.Thumbprint.Replace(" ", string.Empty);
            certificate = store.Certificates
                              .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                              .FirstOrDefault()
                          ?? throw new InvalidOperationException(
                              $"Data Protection certificate {thumbprint} was not found in {options.StoreLocation}/{options.StoreName}.");
        }
        else if (!string.IsNullOrWhiteSpace(options.Path))
        {
            certificate = LoadPfx(options.Path, options.Password);
        }
        else
        {
            throw new InvalidOperationException(
                $"{DataProtectionKeyOptions.SectionName}:KeyEncryption is Certificate but neither " +
                "Certificate:Thumbprint nor Certificate:Path is set.");
        }

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"Data Protection certificate {certificate.Thumbprint} has no private key (keys could not be decrypted).");
        }

        return certificate;
    }

    private static X509Certificate2 LoadPfx(string path, string? password)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Data Protection certificate file '{path}' does not exist.");
        }

        // Linux containers: keep the private key in memory only. Windows needs a persisted key set for EncryptedXml.
        var flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        return X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
    }
}

/// <summary>Outcome of the startup check on key encryption.</summary>
public enum DataProtectionKeyEncryptionStatus
{
    Encrypted,
    UnencryptedAllowed,
    UnencryptedWarning,
    UnencryptedRejected
}

/// <summary>
/// Production guard: outside Development/Testing an unencrypted key ring logs a warning at startup, or stops the
/// application when <see cref="DataProtectionKeyOptions.RequireKeyEncryption"/> is true.
/// </summary>
public sealed class DataProtectionKeyEncryptionCheck(
    IOptions<DataProtectionKeyOptions> options,
    IHostEnvironment environment,
    ILogger<DataProtectionKeyEncryptionCheck> logger) : IHostedService
{
    public const string UnencryptedMessage =
        "Data Protection keys are stored UNENCRYPTED in identity.data_protection_keys. Anyone who can read that table " +
        "can decrypt authenticator (TOTP) keys and forge reset tokens. Configure Identity:DataProtection:Certificate, " +
        "AzureKeyVault or AwsKms (see wiki/Identity/Permissions-2FA-and-Admin-Audit.md) and set Identity:DataProtection:RequireKeyEncryption=true.";

    public static DataProtectionKeyEncryptionStatus Evaluate(DataProtectionKeyOptions options, bool developmentLike)
    {
        if (options.EffectiveKeyEncryption != DataProtectionKeyEncryption.None)
        {
            return DataProtectionKeyEncryptionStatus.Encrypted;
        }

        if (developmentLike)
        {
            return DataProtectionKeyEncryptionStatus.UnencryptedAllowed;
        }

        return options.RequireKeyEncryption
            ? DataProtectionKeyEncryptionStatus.UnencryptedRejected
            : DataProtectionKeyEncryptionStatus.UnencryptedWarning;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        switch (Evaluate(options.Value, environment.IsDevelopmentLike()))
        {
            case DataProtectionKeyEncryptionStatus.UnencryptedRejected:
                throw new InvalidOperationException(
                    "Identity:DataProtection:RequireKeyEncryption is true but no key encryption is configured. " +
                    UnencryptedMessage);
            case DataProtectionKeyEncryptionStatus.UnencryptedWarning:
                logger.LogWarning(UnencryptedMessage);
                break;
            case DataProtectionKeyEncryptionStatus.Encrypted:
                logger.LogInformation("Data Protection keys are encrypted at rest ({Provider})",
                    options.Value.EffectiveKeyEncryption);
                break;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
