using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.OnlinePayments.Features.CreateOnlineCheckout;
using Subscriptions.Options;
using Subscriptions.Resources;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Subscriptions.Tests;

/// <summary>Checkout return URLs are limited to our own front-end origins (no open redirect via the provider).</summary>
public class CreateOnlineCheckoutCommandValidatorTests
{
    private static IStringLocalizer<SubscriptionsResource> Localizer() =>
        new StringLocalizer<SubscriptionsResource>(new ResourceManagerStringLocalizerFactory(
            MsOptions.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static CreateOnlineCheckoutCommandValidator Validator(string[] returnOrigins, string[] corsOrigins)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            corsOrigins.Select((o, i) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", o))).Build();
        return new CreateOnlineCheckoutCommandValidator(
            MsOptions.Create(new SubscriptionOptions { OnlinePaymentReturnOrigins = returnOrigins }), configuration, Localizer());
    }

    private static CreateOnlineCheckoutCommand Command(string success, string cancel) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Fake", success, cancel);

    [Fact]
    public void ReturnUrl_OnConfiguredOrigin_IsAccepted()
    {
        var result = Validator(["https://app.example.com/"], [])
            .Validate(Command("https://app.example.com/billing/ok?x=1", "https://APP.example.com/billing/cancel"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ReturnUrl_OnForeignOrigin_IsRejected()
    {
        var result = Validator(["https://app.example.com"], [])
            .Validate(Command("https://evil.example.net/phish", "https://app.example.com/cancel"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "SuccessUrl");
    }

    [Fact]
    public void ReturnUrl_WithDifferentPortOrScheme_IsRejected()
    {
        var validator = Validator(["https://app.example.com"], []);

        validator.Validate(Command("http://app.example.com/ok", "https://app.example.com/c")).IsValid.Should().BeFalse();
        validator.Validate(Command("https://app.example.com:8443/ok", "https://app.example.com/c")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Without_explicit_origins_the_cors_origins_are_used()
    {
        var validator = Validator([], ["https://www.example.com"]);

        validator.Validate(Command("https://www.example.com/ok", "https://www.example.com/c")).IsValid.Should().BeTrue();
        validator.Validate(Command("https://other.example.com/ok", "https://www.example.com/c")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Without_any_origins_any_absolute_http_url_is_accepted_but_relative_is_not()
    {
        var validator = Validator([], []);

        validator.Validate(Command("https://anything.test/ok", "http://localhost:3000/c")).IsValid.Should().BeTrue();
        validator.Validate(Command("/ok", "http://localhost:3000/c")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReturnUrl_Rejection_UsesTheLocalizedMessage_InBothLanguages()
    {
        var validator = Validator(["https://app.example.com"], []);
        var command = Command("https://evil.example.net/x", "https://app.example.com/c");

        string Message()
        {
            var error = validator.Validate(command).Errors.Single(e => e.PropertyName == "SuccessUrl");
            return error.ErrorMessage;
        }

        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en");
            Message().Should().Be("SuccessUrl must be an absolute http(s) URL on an allowed front-end origin.");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar");
            Message().Should().NotBe("Validation.SuccessUrl.Invalid").And.NotStartWith("SuccessUrl must");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
