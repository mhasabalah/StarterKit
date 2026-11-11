using Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Api.Tests.Infrastructure;

public sealed class DockerSecretsConfigurationProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("secrets-test").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private IConfiguration Build(string environment = "Docker") =>
        new ConfigurationBuilder().AddDockerSecrets(_dir, environment).Build();

    private void Secret(string name, string value) => File.WriteAllText(Path.Combine(_dir, name), value);

    [Fact]
    public void Maps_any_file_using_double_underscore_as_section_separator()
    {
        Secret("Email__SmtpPassword", "smtp-pass\n");

        var config = Build();

        config["Email:SmtpPassword"].Should().Be("smtp-pass", "values are trimmed and __ becomes ':'");
        config["Email__SmtpPassword"].Should().Be("smtp-pass");
    }

    [Theory]
    [InlineData("jwt_secret", "JwtSettings:SecretKey")]
    [InlineData("identity_otp_hmac_key", "Identity:OtpHmacKey")]
    [InlineData("hangfire_dashboard_password", "Hangfire:DashboardAuth:Password")]
    [InlineData("dataprotection_cert_password", "Identity:DataProtection:Certificate:Password")]
    public void Maps_well_known_secret_names(string file, string key)
    {
        Secret(file, "a-very-long-secret-value-0123456789abcdef");

        Build()[key].Should().Be("a-very-long-secret-value-0123456789abcdef");
    }

    [Fact]
    public void Hangfire_dashboard_password_secret_reaches_the_dashboard_options_and_beats_an_empty_env_var()
    {
        Secret("hangfire_dashboard_password", "dashboard-secret");

        // docker-compose.yml passes Hangfire__DashboardAuth__Password: ${HANGFIRE_DASHBOARD_PASSWORD:-} (often empty).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Hangfire:DashboardAuth:Password", "")])
            .AddDockerSecrets(_dir, "Production")
            .Build();

        var options = new HangfireDashboardAuthOptions();
        config.GetSection(HangfireDashboardAuthOptions.SectionName).Bind(options);
        options.Password.Should().Be("dashboard-secret",
            "the secret must land in the section the dashboard filter reads, or Basic auth silently never works");
    }

    [Fact]
    public void Builds_connection_strings_from_passwords()
    {
        Secret("postgres_password", "pg-secret");
        Secret("redis_password", "redis-secret");

        var config = Build();

        config["ConnectionStrings:starterkit"].Should().Contain("Password=pg-secret");
        config["ConnectionStrings:Redis"].Should().Contain("password=redis-secret");
    }

    [Fact]
    public void Binary_certificate_secrets_are_not_loaded_as_configuration()
    {
        File.WriteAllBytes(Path.Combine(_dir, "dataprotection_cert.pfx"), [0x30, 0x82, 0x00, 0x01]);

        var config = Build();

        config["dataprotection_cert.pfx"].Should().BeNull("the PFX is read by path (Identity:DataProtection:Certificate:Path)");
    }

    [Fact]
    public void Key_file_secrets_are_read_by_path_and_never_loaded_as_configuration()
    {
        File.WriteAllText(Path.Combine(_dir, "fcm_service_account.json"), """{"type":"service_account","private_key":"k"}""");

        var config = Build();

        config["fcm_service_account.json"].Should().BeNull("the FCM key is read by path (Push:CredentialsFile)");
        config.AsEnumerable().Should().NotContain(kv => kv.Value != null && kv.Value.Contains("private_key"),
            "a private key must never end up in IConfiguration");
    }

    [Fact]
    public void Skips_everything_in_Development()
    {
        Secret("jwt_secret", "x");

        Build("Development")["JwtSettings:SecretKey"].Should().BeNull();
    }

    [Fact]
    public void Ignores_empty_and_hidden_files_and_missing_directory()
    {
        Secret("empty_secret", "   ");
        Secret(".hidden", "x");

        var config = Build();
        config["empty_secret"].Should().BeNull();
        config[".hidden"].Should().BeNull();

        var missing = new ConfigurationBuilder()
            .AddDockerSecrets(Path.Combine(_dir, "nope"), "Production").Build();
        missing.AsEnumerable().Should().BeEmpty();
    }

    [Fact]
    public void Secret_values_override_appsettings_placeholders()
    {
        Secret("identity_otp_hmac_key", "real-key-real-key-real-key-real-key-1234");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Identity:OtpHmacKey", "CHANGE_ME")])
            .AddDockerSecrets(_dir, "Production")
            .Build();

        ProductionConfigurationGuard.FindPlaceholders(config).Should().BeEmpty();
    }
}
