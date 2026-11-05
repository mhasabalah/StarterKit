using System.Text.RegularExpressions;
using Notification.Channels.Email;

namespace Notification.Tests.Email;

/// <summary>
/// Guards of the email design system: tokens are the only source of color, every template renders in both cultures
/// with the dark-mode and RTL hooks, branding overrides apply, preheaders and the plain-text part are correct.
/// </summary>
public partial class EmailDesignSystemTests
{
    private readonly EmailTestHost _host = new();

    [Fact]
    public void No_liquid_file_hard_codes_a_color()
    {
        var assembly = typeof(FluidTemplateRenderer).Assembly;
        var offenders = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".liquid", StringComparison.Ordinal))
            .Where(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return HexColor().IsMatch(reader.ReadToEnd());
            })
            .ToList();

        offenders.Should().BeEmpty("colors come from EmailDesignTokens ({{ t.<token> }}), never from a template");
    }

    [Theory]
    [MemberData(nameof(EmailTestHost.AllTemplatesAndCultures), MemberType = typeof(EmailTestHost))]
    public async Task Every_template_renders_on_tokens_with_dark_mode_and_direction(EmailTemplate template, NotificationCulture culture)
    {
        var html = await _host.RenderAsync(template, culture);

        html.Should().NotContain("{{").And.NotContain("{%");
        // A misspelled token renders as nothing: "color:;" / "padding: 0;" would expose it.
        Regex.IsMatch(html, @"[a-z-]+:\s*;").Should().BeFalse("every style value comes from an existing token");
        html.Should().Contain("prefers-color-scheme: dark");
        html.Should().Contain(culture == NotificationCulture.Ar ? "dir=\"rtl\"" : "dir=\"ltr\"");
        html.Should().Contain(EmailDesignTokens.Defaults["brand"], "the header is painted with the brand token");
    }

    [Fact]
    public async Task Brand_overrides_repaint_the_header_and_invalid_colors_are_ignored()
    {
        var branded = new EmailTestHost(o =>
        {
            o.Theme.PrimaryColor = "#7C3AED";
            o.Theme.AccentColor = "not-a-color";
            o.Theme.LogoUrl = EmailTestHost.AppBaseUrl + "/logo.png";
        });

        var html = await branded.RenderAsync(EmailTemplate.WelcomeUser, NotificationCulture.En);

        html.Should().Contain("background-color:#7c3aed");
        html.Should().Contain(EmailDesignTokens.Defaults["accent"], "an invalid override keeps the default");
        html.Should().Contain($"src=\"{EmailTestHost.AppBaseUrl}/logo.png\"");
    }

    [Fact]
    public async Task A_template_preheader_becomes_the_inbox_preview_and_never_leaks_the_code()
    {
        var html = await _host.RenderAsync(EmailTemplate.OneTimeCode, NotificationCulture.En,
            name => name == "Code" ? "482913" : null);

        var preheader = Regex.Match(html, "<div style=\"display:none;[^>]*>(?<p>.*?)</div>", RegexOptions.Singleline)
            .Groups["p"].Value;
        preheader.Should().Contain("Your verification code is inside");
        preheader.Should().NotContain("482913");
        html.Should().Contain("482913", "the code itself is in the body");
    }

    [Fact]
    public void Tokens_merge_valid_brand_overrides_only()
    {
        var tokens = EmailDesignTokens.Build(new EmailThemeOptions { PrimaryColor = "#123ABC", OnPrimaryColor = "white" });

        tokens["brand"].Should().Be("#123abc");
        tokens["onBrand"].Should().Be(EmailDesignTokens.Defaults["onBrand"]);
        EmailDesignTokens.Defaults.Values.Where(v => v.StartsWith('#'))
            .Should().OnlyContain(v => EmailDesignTokens.IsHexColor(v));
    }

    [Theory]
    [MemberData(nameof(EmailTestHost.AllTemplatesAndCultures), MemberType = typeof(EmailTestHost))]
    public async Task The_plain_text_alternative_is_readable(EmailTemplate template, NotificationCulture culture)
    {
        var text = EmailPlainText.FromHtml(await _host.RenderAsync(template, culture));

        text.Should().NotBeNullOrWhiteSpace();
        text.Should().NotContain("<").And.NotContain("&nbsp;").And.NotContain("prefers-color-scheme");
        text.Should().NotContain("\n\n\n");
    }

    [Fact]
    public void Plain_text_keeps_link_targets_and_drops_markup()
    {
        var text = EmailPlainText.FromHtml(
            "<html><head><style>.x{}</style></head><body><div style=\"display:none\">hidden</div>" +
            "<h2>Title</h2><p>Hello &amp; welcome</p><a href=\"https://app.test/go\">Open</a>" +
            "<a href=\"mailto:help@x.test\">help@x.test</a><!-- comment --></body></html>");

        text.Should().Be("Title\nHello & welcome\nOpen (https://app.test/go)help@x.test");
    }

    [GeneratedRegex("#[0-9a-fA-F]{6}\\b|#[0-9a-fA-F]{3}\\b")]
    private static partial Regex HexColor();
}
