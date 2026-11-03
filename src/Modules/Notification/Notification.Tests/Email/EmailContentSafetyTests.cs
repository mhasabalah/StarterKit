namespace Notification.Tests.Email;

public sealed class EmailContentSafetyTests
{
    private static readonly EmailOptions Options = new()
    {
        AppBaseUrl = "https://app.starterkit.test",
        AllowedLinkHosts = ["cdn.starterkit.test"]
    };

    [Theory]
    [InlineData("https://app.starterkit.test/reset?token=abc")]
    [InlineData("http://app.starterkit.test/x")]
    [InlineData("https://APP.starterkit.test/upper")]
    [InlineData("https://cdn.starterkit.test/file.pdf")]
    public void Allowed_urls_pass_through(string url)
        => EmailContentSafety.SanitizeUrl(url, Options).Should().NotBe(EmailContentSafety.RejectedUrl);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("mailto:victim@example.com")]
    [InlineData("ftp://app.starterkit.test/file")]
    [InlineData("/relative/path")]
    [InlineData("//evil.test/path")]
    [InlineData("https://evil.test/reset")]
    [InlineData("https://app.starterkit.test.evil.test/")]
    [InlineData("https://app.starterkit.test@evil.test/")]
    [InlineData("https://user:pass@app.starterkit.test/")]
    [InlineData("https://app.starterkit.test/a\nb")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("\" onmouseover=\"alert(1)")]
    public void Unsafe_urls_are_rejected(string? url)
        => EmailContentSafety.SanitizeUrl(url, Options).Should().Be(EmailContentSafety.RejectedUrl);

    [Theory]
    [InlineData("Hello\r\nBcc: victim@example.com", "Hello Bcc: victim@example.com")]
    [InlineData("Hello\nWorld", "Hello World")]
    [InlineData("  Tabs\t\tand  spaces ", "Tabs and spaces")]
    [InlineData("Null\0byte", "Null byte")]
    [InlineData("مرحباً بك", "مرحباً بك")]
    public void Subject_cr_lf_and_control_chars_are_stripped(string input, string expected)
        => EmailContentSafety.SanitizeSubject(input).Should().Be(expected);

    [Fact]
    public void Unicode_line_separators_are_stripped_from_subject()
        => EmailContentSafety.SanitizeSubject("Line" + (char)0x2028 + "Sep" + (char)0x2029 + "End")
            .Should().Be("Line Sep End");

    [Fact]
    public void Subject_is_capped()
        => EmailContentSafety.SanitizeSubject(new string('x', 1000)).Length.Should().Be(255);

    [Theory]
    [InlineData("ahmed@example.com", "a***@example.com")]
    [InlineData(null, "none")]
    [InlineData("not-an-email", "***")]
    public void Emails_are_masked(string? input, string expected)
        => PiiMask.Email(input).Should().Be(expected);

    [Theory]
    [InlineData("+201001234567", "*********4567")]
    [InlineData("123", "***")]
    [InlineData(null, "none")]
    public void Phones_are_masked(string? input, string expected)
        => PiiMask.Phone(input).Should().Be(expected);
}
