using Webhooks.Options;
using Webhooks.Security;

namespace Webhooks.Tests.Security;

public class WebhookUrlPolicyTests
{
    private static WebhookUrlCheck Check(string? url, Action<WebhooksOptions>? configure = null) =>
        TestSupport.UrlPolicy(configure).Check(url);

    [Fact]
    public void A_public_https_url_is_accepted() =>
        Check("https://hooks.example.com/in?x=1").Should().Be(WebhookUrlCheck.Ok);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_is_rejected(string? url) => Check(url).Should().Be(WebhookUrlCheck.Empty);

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://hooks.example.com/in")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://hooks.example.com/")]
    public void Only_absolute_http_and_https_urls_are_accepted(string url) =>
        Check(url).Should().Be(WebhookUrlCheck.Invalid);

    [Fact]
    public void Plain_http_is_rejected_by_default() =>
        Check("http://hooks.example.com/in").Should().Be(WebhookUrlCheck.HttpsRequired);

    [Fact]
    public void Plain_http_is_accepted_when_https_is_not_required() =>
        Check("http://hooks.example.com/in", o => o.RequireHttps = false).Should().Be(WebhookUrlCheck.Ok);

    [Fact]
    public void Credentials_in_the_url_are_rejected() =>
        Check("https://user:pass@hooks.example.com/in").Should().Be(WebhookUrlCheck.HasCredentials);

    [Theory]
    [InlineData("https://127.0.0.1/in")]
    [InlineData("https://10.0.0.5/in")]
    [InlineData("https://192.168.1.10:8443/in")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/in")]
    [InlineData("https://[fd00::1]/in")]
    [InlineData("https://[::ffff:10.0.0.5]/in")]
    [InlineData("https://localhost/in")]
    [InlineData("https://LOCALHOST:8080/in")]
    [InlineData("https://app.localhost/in")]
    [InlineData("https://printer.local/in")]
    [InlineData("https://db.internal/in")]
    [InlineData("https://intranet/in")]
    public void Literal_private_hosts_and_internal_names_are_rejected(string url) =>
        Check(url).Should().Be(WebhookUrlCheck.BlockedHost);

    [Theory]
    [InlineData("https://127.0.0.1:5099/in")]
    [InlineData("https://localhost/in")]
    [InlineData("https://10.0.0.5/in")]
    public void The_development_toggle_allows_private_targets(string url) =>
        Check(url, o => o.AllowPrivateNetworks = true).Should().Be(WebhookUrlCheck.Ok);

    [Fact]
    public void A_very_long_url_is_rejected() =>
        Check("https://hooks.example.com/" + new string('a', 3000)).Should().Be(WebhookUrlCheck.TooLong);
}
