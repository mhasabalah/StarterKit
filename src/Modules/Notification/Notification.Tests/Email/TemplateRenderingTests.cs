using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Notification.Tests.Email;

public sealed class TemplateRenderingTests
{
    private readonly EmailTestHost _host = new();

    private const string ScriptPayload = "<script>alert('xss')</script>";
    private const string LinkPayload = "<a href=\"https://evil.test/login\">Click</a>";
    private const string InjectionPayload = ScriptPayload + LinkPayload + " & \"quoted\"";

    [Theory]
    [MemberData(nameof(EmailTestHost.AllTemplatesAndCultures), MemberType = typeof(EmailTestHost))]
    public async Task Every_template_renders_in_every_culture_with_a_populated_model(EmailTemplate template, NotificationCulture culture)
    {
        var html = await _host.RenderAsync(template, culture);

        html.Should().StartWith("<!DOCTYPE html");
        html.Should().Contain("</html>");
        html.Should().Contain(culture == NotificationCulture.Ar ? "lang=\"ar\"" : "lang=\"en\"");
        html.Should().Contain("StarterKit", "the layout header shows the brand name");
        html.Should().NotContain("{{").And.NotContain("{%", "no Liquid markup may leak into the output");

        // Renderer-produced HTML (layout body, captured card content) must NOT be double-encoded.
        html.Should().NotContain("&lt;p").And.NotContain("&lt;table").And.NotContain("&lt;tr")
            .And.NotContain("&lt;a ").And.NotContain("&lt;h2");
    }

    [Theory]
    [MemberData(nameof(EmailTestHost.AllTemplatesAndCultures), MemberType = typeof(EmailTestHost))]
    public async Task Html_in_user_supplied_values_is_encoded(EmailTemplate template, NotificationCulture culture)
    {
        var html = await _host.RenderAsync(template, culture,
            name => name.EndsWith("Url", StringComparison.Ordinal) ? null : InjectionPayload);

        html.Should().NotContain("<script", "user values must never become markup");
        html.Should().NotContain("href=\"https://evil.test");

        // Every template with a text field renders at least one of them - escaped.
        var modelType = _host.Registry.GetModelType(template)!;
        var hasTextField = modelType.GetProperties()
            .Any(p => p.PropertyType == typeof(string) && !p.Name.EndsWith("Url", StringComparison.Ordinal));
        if (hasTextField)
        {
            html.Should().Contain("&lt;script&gt;");
            html.Should().Contain("&lt;a href=&quot;https://evil.test/login&quot;&gt;");
        }
    }

    [Fact]
    public async Task Rejection_reason_is_escaped_inside_the_card_component()
    {
        var html = await _host.RenderAsync(EmailTemplate.PaymentRejected, NotificationCulture.En,
            name => name == "RejectionReason" ? InjectionPayload : null);

        html.Should().Contain("&lt;script&gt;alert(&#x27;xss&#x27;)&lt;/script&gt;");
        html.Should().Contain("&amp; &quot;quoted&quot;");
        html.Should().Contain("<p style=\"margin:0;font-size:14px;color:#374151;line-height:1.75;\">",
            "the card wrapper itself is trusted template HTML and stays markup");
    }

    [Theory]
    [MemberData(nameof(EmailTestHost.TemplatesWithLinks), MemberType = typeof(EmailTestHost))]
    public async Task Allowed_links_are_rendered_into_the_button_href(EmailTemplate template)
    {
        var html = await _host.RenderAsync(template, NotificationCulture.En);

        Regex.Matches(html, "href=\"(?<u>[^\"]*)\"")
            .Select(m => m.Groups["u"].Value)
            .Where(u => !u.StartsWith("mailto:", StringComparison.Ordinal))
            .Should().NotBeEmpty()
            .And.OnlyContain(u => u.StartsWith(EmailTestHost.AppBaseUrl + "/sample/", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EmailTestHost.TemplatesWithLinks), MemberType = typeof(EmailTestHost))]
    public async Task Unsafe_links_are_replaced_in_the_button_href(EmailTemplate template)
    {
        foreach (var bad in new[] { "javascript:alert(1)", "https://evil.test/reset", "\" onmouseover=\"alert(1)" })
        {
            var html = await _host.RenderAsync(template, NotificationCulture.En,
                name => name.EndsWith("Url", StringComparison.Ordinal) ? bad : null);

            html.Should().NotContain("javascript:");
            html.Should().NotContain("evil.test");
            html.Should().NotContain("onmouseover=\"");
            html.Should().Contain("href=\"#\"");
        }
    }

    [Fact]
    public async Task Arabic_text_is_not_turned_into_numeric_entities()
    {
        var html = await _host.RenderAsync(EmailTemplate.WelcomeUser, NotificationCulture.Ar, _ => "أحمد");

        html.Should().Contain("أحمد");
        html.Should().Contain("dir=\"rtl\"");
    }

    [Fact]
    public void Smoke_render_succeeds_for_every_template()
    {
        var failures = NotificationModule.SmokeRenderAllTemplates(
            _host.Renderer, _host.Samples, NullLogger.Instance, throwOnFailure: true);

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Smoke_render_throws_in_strict_mode_and_only_reports_in_lenient_mode()
    {
        var broken = new Mock<IEmailTemplateRenderer>();
        broken.Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
                It.IsAny<NotificationCulture>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var strict = () => NotificationModule.SmokeRenderAllTemplates(
            broken.Object, _host.Samples, NullLogger.Instance, throwOnFailure: true);
        strict.Should().Throw<AggregateException>();

        var lenient = NotificationModule.SmokeRenderAllTemplates(
            broken.Object, _host.Samples, NullLogger.Instance, throwOnFailure: false);
        lenient.Should().HaveCount(Enum.GetValues<EmailTemplate>().Length * Enum.GetValues<NotificationCulture>().Length);
    }

    [Theory]
    [MemberData(nameof(EmailTestHost.AllTemplatesAndCultures), MemberType = typeof(EmailTestHost))]
    public void Every_subject_is_single_line_and_non_empty(EmailTemplate template, NotificationCulture culture)
    {
        var subject = _host.Registry.ResolveSubject(template, culture);

        subject.Should().NotBeNullOrWhiteSpace();
        subject.Should().NotContainAny("\r", "\n");
    }
}
