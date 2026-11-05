using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Identity.Infrastructure.DataProtection;
using Identity.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Infrastructure;

/// <summary>Config-driven at-rest encryption of the Data Protection key ring and the production guard.</summary>
public sealed class DataProtectionKeyEncryptionTests : IDisposable
{
    private const string PfxPassword = "test-pfx-password";
    private readonly string _dir = Directory.CreateTempSubdirectory("sk-dp-").FullName;

    public void Dispose()
    {
        try
        { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private string CreatePfx(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_dir, $"{name}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, PfxPassword));
        return path;
    }

    private IDataProtectionProvider BuildProvider(string keyDir, DataProtectionKeyOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyDir));
        IdentityDataProtectionExtensions.ConfigureKeyEncryption(builder, options);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private static DataProtectionKeyOptions WithPfx(string path, params string[] previous) => new()
    {
        Certificate = new DataProtectionCertificateOptions { Path = path, Password = PfxPassword, PreviousPaths = previous }
    };

    [Fact]
    public void A_configured_pfx_encrypts_the_key_ring_and_a_second_instance_can_read_it()
    {
        var keyDir = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        var options = WithPfx(CreatePfx("current"));
        options.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.Certificate, "a certificate turns it on");

        var payload = BuildProvider(keyDir, options).CreateProtector("p").Protect("secret");

        var xml = string.Concat(Directory.GetFiles(keyDir, "*.xml").Select(File.ReadAllText));
        xml.Should().Contain("encryptedSecret").And.NotContain("<masterKey", "the master key must not be stored in clear");
        BuildProvider(keyDir, options).CreateProtector("p").Unprotect(payload).Should().Be("secret");
    }

    [Fact]
    public void After_a_certificate_rotation_keys_of_the_previous_certificate_stay_readable()
    {
        var keyDir = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        var old = CreatePfx("old");
        var payload = BuildProvider(keyDir, WithPfx(old)).CreateProtector("p").Protect("secret");

        BuildProvider(keyDir, WithPfx(CreatePfx("new"), old)).CreateProtector("p").Unprotect(payload).Should().Be("secret");
    }

    [Fact]
    public void Misconfiguration_fails_fast()
    {
        FluentActions.Invoking(() => IdentityDataProtectionExtensions.LoadCertificate(
                new DataProtectionCertificateOptions { Path = Path.Combine(_dir, "missing.pfx") }))
            .Should().Throw<InvalidOperationException>().WithMessage("*does not exist*");

        FluentActions.Invoking(() => IdentityDataProtectionExtensions.LoadCertificate(new DataProtectionCertificateOptions()))
            .Should().Throw<InvalidOperationException>();

        FluentActions.Invoking(() => IdentityDataProtectionExtensions.LoadCertificate(new DataProtectionCertificateOptions
        {
            Thumbprint = "0000000000000000000000000000000000000000"
        }))
            .Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void No_certificate_means_no_key_encryption_and_a_shared_application_name()
    {
        var options = new DataProtectionKeyOptions();
        options.EffectiveKeyEncryption.Should().Be(DataProtectionKeyEncryption.None);
        options.ApplicationName.Should().Be("StarterKit");
        options.RequireKeyEncryption.Should().BeFalse("default: warn, so deployments without a certificate keep starting");
    }

    [Theory]
    [InlineData(true, false, false, DataProtectionKeyEncryptionStatus.UnencryptedAllowed)]
    [InlineData(true, false, true, DataProtectionKeyEncryptionStatus.UnencryptedAllowed)]
    [InlineData(false, false, false, DataProtectionKeyEncryptionStatus.UnencryptedWarning)]
    [InlineData(false, false, true, DataProtectionKeyEncryptionStatus.UnencryptedRejected)]
    [InlineData(false, true, true, DataProtectionKeyEncryptionStatus.Encrypted)]
    public void Production_guard(bool developmentLike, bool encrypted, bool required, DataProtectionKeyEncryptionStatus expected)
    {
        var options = new DataProtectionKeyOptions
        {
            RequireKeyEncryption = required,
            KeyEncryption = encrypted ? DataProtectionKeyEncryption.Certificate : null
        };

        DataProtectionKeyEncryptionCheck.Evaluate(options, developmentLike).Should().Be(expected);
    }
}
