using System.Net;
using System.Text;
using MailKit.Net.Smtp;

namespace Notification.Tests.Dispatch;

public sealed class ChannelAndProviderTests
{
    private static readonly INotificationChannelAdapter[] Adapters =
    [
        new FakeChannelAdapter(NotificationChannel.WhatsApp),
        new FakeChannelAdapter(NotificationChannel.Email)
    ];

    [Fact]
    public void Preference_with_a_channel_that_has_no_adapter_is_rejected()
        => ChannelOrderRules.Validate([NotificationChannel.Sms, NotificationChannel.Email], Adapters)
            .Should().Contain("Sms");

    [Theory]
    [InlineData(new NotificationChannel[0], "empty")]
    [InlineData(new[] { NotificationChannel.Email, NotificationChannel.Email }, "duplicates")]
    [InlineData(new[] { (NotificationChannel)99 }, "unknown")]
    public void Invalid_preferences_are_rejected(NotificationChannel[] order, string expected)
        => ChannelOrderRules.Validate(order, Adapters).Should().Contain(expected);

    [Fact]
    public void Valid_preference_passes()
        => ChannelOrderRules.Validate([NotificationChannel.Email, NotificationChannel.WhatsApp], Adapters)
            .Should().BeNull();

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    public async Task WhatsApp_client_captures_the_error_body_and_classifies_status(HttpStatusCode status, bool permanent)
    {
        const string body = "{\"error\":{\"message\":\"(#132001) Template name does not exist in the translation\"}}";
        var client = new WhatsAppApiClient(new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri("https://graph.example.test/v25.0/")
        });

        var act = () => client.SendTemplateAsync("123", "+201001234567", "tpl", "ar", ["x"], CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<WhatsAppApiException>()).Which;
        ex.StatusCode.Should().Be(status);
        ex.ResponseBody.Should().Be(body);
        ex.Message.Should().Contain("Template name does not exist");
        ex.IsPermanent.Should().Be(permanent);
        DeliveryFailureClassifier.Classify(ex).Should().Be(permanent ? DeliveryFailureKind.Permanent : DeliveryFailureKind.Transient);
    }

    [Fact]
    public async Task WhatsApp_client_succeeds_on_2xx()
    {
        var client = new WhatsAppApiClient(new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"))
        {
            BaseAddress = new Uri("https://graph.example.test/v25.0/")
        });

        await FluentActions.Invoking(() => client.SendTemplateAsync("1", "+1", "tpl", "en", [], CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void Failure_classification()
    {
        DeliveryFailureClassifier.Classify(new TaskCanceledException()).Should().Be(DeliveryFailureKind.Ambiguous);
        DeliveryFailureClassifier.Classify(new Polly.Timeout.TimeoutRejectedException()).Should().Be(DeliveryFailureKind.Ambiguous);
        DeliveryFailureClassifier.Classify(new HttpRequestException("refused")).Should().Be(DeliveryFailureKind.Transient);
        DeliveryFailureClassifier.Classify(new MissingWhatsAppTemplateException("x")).Should().Be(DeliveryFailureKind.Permanent);
        DeliveryFailureClassifier.Classify(
                new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550"))
            .Should().Be(DeliveryFailureKind.Permanent);
        DeliveryFailureClassifier.Classify(
                new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.MailboxBusy, "450"))
            .Should().Be(DeliveryFailureKind.Transient);
    }

    [Fact]
    public void WhatsApp_options_require_a_meta_template_name_for_every_template()
    {
        var complete = CompleteOptions();
        new WhatsAppOptionsValidator().Validate(null, complete).Succeeded.Should().BeTrue();

        var blank = CompleteOptions();
        blank.Templates[WhatsAppTemplateRegistry.GetKey(WhatsAppTemplate.TwoFactorOtp)].MetaTemplateName = " ";
        var result = new WhatsAppOptionsValidator().Validate(null, blank);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("two_factor_otp:MetaTemplateName");

        var missing = CompleteOptions();
        missing.Templates.Remove(WhatsAppTemplateRegistry.GetKey(WhatsAppTemplate.PaymentRejected));
        new WhatsAppOptionsValidator().Validate(null, missing).FailureMessage.Should().Contain("payment_rejected");

        FluentActions.Invoking(() => new WhatsAppTemplateRegistry(blank)).Should().Throw<InvalidOperationException>();
    }

    private static WhatsAppOptions CompleteOptions() => new()
    {
        Templates = Enum.GetValues<WhatsAppTemplate>().ToDictionary(
            WhatsAppTemplateRegistry.GetKey,
            t => new WhatsAppTemplateConfig { MetaTemplateName = WhatsAppTemplateRegistry.GetKey(t), LanguageCode = "ar" })
    };

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
