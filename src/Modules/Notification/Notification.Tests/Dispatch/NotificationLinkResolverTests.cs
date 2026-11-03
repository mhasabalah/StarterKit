using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Dispatch;

public sealed class NotificationLinkResolverTests
{
    private static readonly NotificationLinkResolver Links =
        new(MsOptions.Create(new EmailOptions { AppBaseUrl = "https://app.example.com/" }));

    [Theory]
    [InlineData("/invitations/accept?token=a%2Bb", "https://app.example.com/invitations/accept?token=a%2Bb")]
    [InlineData("invitations/x", "https://app.example.com/invitations/x")]
    [InlineData("https://portal.example.com/join?t=1", "https://portal.example.com/join?t=1")]
    [InlineData("http://localhost:4200/join", "http://localhost:4200/join")]
    public void Relative_links_are_resolved_against_the_app_url_absolute_ones_kept(string url, string expected)
    {
        Links.ToAbsolute(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://files.example.com/x")]
    public void Non_http_schemes_are_never_returned_as_absolute_links(string url)
    {
        Links.ToAbsolute(url).Should().StartWith("https://app.example.com/");
    }
}
