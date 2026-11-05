using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Identity.Infrastructure.DataProtection;
using Identity.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Identity.Tests.Infrastructure;

/// <summary>KMS-backed key ring encryption (Azure Key Vault, AWS KMS) with mocked cloud clients.</summary>
public sealed class DataProtectionKmsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sk-dp-kms-").FullName;

    public void Dispose()
    {
        try
        { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A fake KMS: "encrypts" by prefixing and binding the encryption context, refuses a different context.</summary>
    private sealed class FakeKms
    {
        public Mock<IAmazonKeyManagementService> Client { get; } = new();
        public List<EncryptRequest> Encrypts { get; } = [];
        public List<DecryptRequest> Decrypts { get; } = [];

        public FakeKms()
        {
            Client.Setup(k => k.EncryptAsync(It.IsAny<EncryptRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EncryptRequest r, CancellationToken _) =>
                {
                    Encrypts.Add(r);
                    var plaintext = r.Plaintext.ToArray();
                    var context = string.Join(";", r.EncryptionContext.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
                    var blob = System.Text.Encoding.UTF8.GetBytes($"kms|{r.KeyId}|{context}|")
                        .Concat(plaintext.Select(b => (byte)(b ^ 0x5A))).ToArray();
                    return new EncryptResponse { KeyId = $"arn:aws:kms:eu-central-1:111122223333:key/{r.KeyId}", CiphertextBlob = new MemoryStream(blob) };
                });

            Client.Setup(k => k.DecryptAsync(It.IsAny<DecryptRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DecryptRequest r, CancellationToken _) =>
                {
                    Decrypts.Add(r);
                    var blob = r.CiphertextBlob.ToArray();
                    var text = System.Text.Encoding.UTF8.GetString(blob, 0, Math.Min(blob.Length, 400));
                    var parts = text.Split('|', 4);
                    var context = string.Join(";", r.EncryptionContext.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
                    if (parts[2] != context)
                        throw new InvalidCiphertextException("encryption context mismatch");
                    var header = System.Text.Encoding.UTF8.GetByteCount(string.Join("|", parts[0], parts[1], parts[2]) + "|");
                    return new DecryptResponse
                    {
                        Plaintext = new MemoryStream(blob.Skip(header).Select(b => (byte)(b ^ 0x5A)).ToArray())
                    };
                });
        }
    }

    private static DataProtectionKeyOptions AwsOptions(string app = "StarterKit") => new()
    {
        ApplicationName = app,
        AwsKms = new DataProtectionAwsKmsOptions { KeyId = "alias/starterkit-dp", Region = "eu-central-1" }
    };

    private IServiceProvider Build(string keyDir, DataProtectionKeyOptions options, FakeKms? kms = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DataProtectionKeyOptions>(o => o.ApplicationName = options.ApplicationName);
        if (kms is not null)
            services.AddSingleton(kms.Client.Object);
        var builder = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyDir));
        IdentityDataProtectionExtensions.ConfigureKeyEncryption(builder, options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Aws_kms_encrypts_the_key_ring_and_a_second_instance_reads_it_back()
    {
        var keyDir = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        var kms = new FakeKms();
        var options = AwsOptions();
        options.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.AwsKms, "a KMS key id turns it on");

        var payload = Build(keyDir, options, kms).GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("p").Protect("secret");

        var xml = string.Concat(Directory.GetFiles(keyDir, "*.xml").Select(File.ReadAllText));
        xml.Should().Contain("awsKmsEncryptedKey").And.NotContain("<masterKey", "the master key must not be stored in clear");
        kms.Encrypts.Should().NotBeEmpty();
        kms.Encrypts[0].KeyId.Should().Be("alias/starterkit-dp");
        kms.Encrypts[0].EncryptionContext["application"].Should().Be("StarterKit");

        var second = new FakeKms();
        // Same "KMS" state is not needed: the fake is stateless apart from the blob contents.
        Build(keyDir, options, second).GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("p").Unprotect(payload).Should().Be("secret");
        second.Decrypts.Should().NotBeEmpty("the second instance had to ask KMS to decrypt the key");
    }

    [Fact]
    public void Aws_kms_ciphertext_is_bound_to_the_application_name()
    {
        var keyDir = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        var payload = Build(keyDir, AwsOptions("app-a"), new FakeKms()).GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("p").Protect("secret");

        // Another application (different name => different encryption context and key purpose) cannot read the key.
        var other = Build(keyDir, AwsOptions("app-b"), new FakeKms()).GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("p");

        FluentActions.Invoking(() => other.Unprotect(payload)).Should().Throw<Exception>();
    }

    [Fact]
    public void Aws_kms_without_a_key_id_fails_fast()
    {
        var options = new DataProtectionKeyOptions { KeyEncryption = DataProtectionKeyEncryption.AwsKms };

        FluentActions.Invoking(() => Build(Path.Combine(_dir, "k"), options))
            .Should().Throw<InvalidOperationException>().WithMessage("*AwsKms:KeyId*");
    }

    [Fact]
    public void Aws_encryptor_and_decryptor_round_trip_an_element_through_the_kms_client()
    {
        var kms = new FakeKms();
        var element = new System.Xml.Linq.XElement("masterKey", "c2VjcmV0");

        var info = new AwsKmsXmlEncryptor(kms.Client.Object, "key-1", "StarterKit").Encrypt(element);

        info.DecryptorType.Should().Be<AwsKmsXmlDecryptor>();
        info.EncryptedElement.Name.LocalName.Should().Be(AwsKmsXmlEncryptor.ElementName);
        info.EncryptedElement.ToString().Should().NotContain("c2VjcmV0", "no plaintext in the stored element");

        var services = new ServiceCollection()
            .AddSingleton(kms.Client.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new DataProtectionKeyOptions { ApplicationName = "StarterKit" }))
            .BuildServiceProvider();
        new AwsKmsXmlDecryptor(services).Decrypt(info.EncryptedElement).ToString().Should().Be(element.ToString());
    }

    [Fact]
    public void Aws_decryptor_without_a_registered_client_explains_itself()
    {
        FluentActions.Invoking(() => new AwsKmsXmlDecryptor(new ServiceCollection().BuildServiceProvider()))
            .Should().Throw<InvalidOperationException>().WithMessage("*not configured*");
    }

    [Fact]
    public void Azure_key_vault_installs_the_vault_encryptor_on_the_key_ring()
    {
        var options = new DataProtectionKeyOptions
        {
            AzureKeyVault = new DataProtectionAzureKeyVaultOptions
            {
                KeyIdentifier = "https://example-vault.vault.azure.net/keys/dataprotection",
                TenantId = "00000000-0000-0000-0000-000000000001",
                ClientId = "00000000-0000-0000-0000-000000000002",
                ClientSecret = "CHANGE_ME"
            }
        };
        options.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.AzureKeyVault);

        var provider = Build(Path.Combine(_dir, "keys"), options);

        provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor
            .Should().NotBeNull().And.Subject.GetType().Name.Should().Contain("AzureKeyVault");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("http://example-vault.vault.azure.net/keys/k")]
    public void Azure_key_vault_requires_an_https_key_identifier(string? identifier)
    {
        var options = new DataProtectionKeyOptions
        {
            KeyEncryption = DataProtectionKeyEncryption.AzureKeyVault,
            AzureKeyVault = new DataProtectionAzureKeyVaultOptions { KeyIdentifier = identifier }
        };

        FluentActions.Invoking(() => Build(Path.Combine(_dir, "keys"), options))
            .Should().Throw<InvalidOperationException>().WithMessage("*KeyIdentifier*");
    }

    [Fact]
    public void Azure_credential_is_a_client_secret_only_when_fully_configured()
    {
        IdentityDataProtectionExtensions.CreateAzureCredential(new DataProtectionAzureKeyVaultOptions
        {
            TenantId = "t", ClientId = "c", ClientSecret = "s"
        }).Should().BeOfType<Azure.Identity.ClientSecretCredential>();

        IdentityDataProtectionExtensions.CreateAzureCredential(new DataProtectionAzureKeyVaultOptions { TenantId = "t", ClientId = "c" })
            .Should().BeOfType<Azure.Identity.DefaultAzureCredential>("managed identity is the default");
    }

    [Fact]
    public void Provider_detection_prefers_explicit_choice_then_certificate_then_azure_then_aws()
    {
        new DataProtectionKeyOptions().EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.None);

        var all = new DataProtectionKeyOptions
        {
            Certificate = new DataProtectionCertificateOptions { Path = "x.pfx" },
            AzureKeyVault = new DataProtectionAzureKeyVaultOptions { KeyIdentifier = "https://v.vault.azure.net/keys/k" },
            AwsKms = new DataProtectionAwsKmsOptions { KeyId = "k" }
        };
        all.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.Certificate);

        all.KeyEncryption = DataProtectionKeyEncryption.AwsKms;
        all.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.AwsKms);

        all.KeyEncryption = null;
        all.Certificate = new();
        all.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.AzureKeyVault);
    }

    [Theory]
    [InlineData(DataProtectionKeyEncryption.AzureKeyVault)]
    [InlineData(DataProtectionKeyEncryption.AwsKms)]
    public void A_kms_provider_counts_as_encrypted_for_the_production_guard(DataProtectionKeyEncryption provider)
    {
        var options = new DataProtectionKeyOptions { KeyEncryption = provider, RequireKeyEncryption = true };

        DataProtectionKeyEncryptionCheck.Evaluate(options, developmentLike: false)
            .Should().Be(DataProtectionKeyEncryptionStatus.Encrypted);
    }
}
