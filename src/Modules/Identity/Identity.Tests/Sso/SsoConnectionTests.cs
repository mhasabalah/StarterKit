using Identity.Contracts.Sso;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Services.Sso;
using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Tests.Sso;

public class SsoConnectionTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static SsoConnectionInput Oidc(bool complete = true) => new()
    {
        Name = "Acme Entra",
        Protocol = SsoProtocol.Oidc,
        OidcAuthority = "https://login.microsoftonline.com/acme/v2.0/",
        OidcClientId = complete ? "client" : null
    };

    private static SsoConnection Create(bool complete = true) =>
        SsoConnection.Create(Guid.NewGuid(), Oidc(complete), complete ? "protected-secret" : null);

    [Fact]
    public void A_new_connection_is_a_draft_with_a_normalized_authority()
    {
        var connection = Create();

        connection.Status.Should().Be(SsoConnectionStatus.Draft);
        connection.OidcAuthority.Should().Be("https://login.microsoftonline.com/acme/v2.0");
    }

    [Fact]
    public void Activation_needs_a_complete_configuration()
    {
        var connection = Create(complete: false);
        connection.AddDomain("acme.example");
        connection.MarkDomainVerified("acme.example", Now);

        connection.Invoking(c => c.Activate()).Should().Throw<InvalidSsoConnectionException>().WithMessage("*client id*");
    }

    [Fact]
    public void Activation_needs_a_verified_domain()
    {
        var connection = Create();
        connection.AddDomain("acme.example");

        connection.Invoking(c => c.Activate()).Should().Throw<InvalidSsoConnectionException>().WithMessage("*Verify*");

        connection.MarkDomainVerified("acme.example", Now);
        connection.Activate();
        connection.Status.Should().Be(SsoConnectionStatus.Active);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("outlook.com")]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    public void Public_or_invalid_domains_are_refused(string domain) =>
        Create().Invoking(c => c.AddDomain(domain)).Should().Throw<InvalidSsoConnectionException>();

    [Fact]
    public void Only_verified_domains_cover_an_email()
    {
        var connection = Create();
        connection.AddDomain("@Acme.Example.");
        connection.AddDomain("acme-labs.example");
        connection.MarkDomainVerified("acme.example", Now);

        connection.CoversVerified("ada@ACME.example").Should().BeTrue();
        connection.CoversVerified("bob@acme-labs.example").Should().BeFalse("not verified yet");
        connection.CoversVerified("eve@evil.example").Should().BeFalse();
        connection.CoversVerified(null).Should().BeFalse();
        connection.CoversVerified("no-at-sign").Should().BeFalse();
    }

    [Fact]
    public void The_last_verified_domain_of_an_active_connection_cannot_be_removed()
    {
        var connection = Create();
        connection.AddDomain("acme.example");
        connection.MarkDomainVerified("acme.example", Now);
        connection.Activate();

        connection.Invoking(c => c.RemoveDomain("acme.example")).Should().Throw<InvalidSsoConnectionException>();

        connection.Disable();
        connection.RemoveDomain("acme.example");
        connection.Domains.Should().BeEmpty();
    }

    [Fact]
    public void The_protocol_is_fixed_and_a_null_secret_keeps_the_stored_one()
    {
        var connection = Create();

        connection.Invoking(c => c.Update(Oidc() with { Protocol = SsoProtocol.Saml }, null))
            .Should().Throw<InvalidSsoConnectionException>();

        connection.Update(Oidc() with { Name = "Renamed" }, protectedClientSecret: null);
        connection.Name.Should().Be("Renamed");
        connection.OidcClientSecretProtected.Should().Be("protected-secret");
    }

    [Fact]
    public void An_active_connection_cannot_be_edited_into_an_unusable_state()
    {
        var connection = Create();
        connection.AddDomain("acme.example");
        connection.MarkDomainVerified("acme.example", Now);
        connection.Activate();

        connection.Invoking(c => c.Update(Oidc(complete: false), null)).Should().Throw<InvalidSsoConnectionException>();
    }

    [Fact]
    public void Invalid_certificates_are_refused()
    {
        var input = new SsoConnectionInput
        {
            Name = "Acme", Protocol = SsoProtocol.Saml, SamlIdpCertificates = ["not-a-certificate"]
        };

        FluentActions.Invoking(() => SsoConnection.Create(Guid.NewGuid(), input, null))
            .Should().Throw<InvalidSsoConnectionException>();
    }

    [Theory]
    [InlineData("http://localhost:3000/auth/sso", true)]
    [InlineData("http://localhost:3000/auth/sso?next=/x", true)]
    [InlineData("https://evil.example/auth", false)]
    [InlineData("http://localhost:3000@evil.example/", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/relative", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://localhost:3000/#token", false)]
    public void Return_urls_must_be_on_an_allowed_origin(string url, bool allowed)
    {
        var policy = new SsoReturnUrlPolicy(
            Microsoft.Extensions.Options.Options.Create(new SsoOptions { AllowedReturnOrigins = ["http://localhost:3000"] }),
            Microsoft.Extensions.Options.Options.Create(new ApplicationOptions()));

        policy.IsAllowed(url).Should().Be(allowed);
    }
}
