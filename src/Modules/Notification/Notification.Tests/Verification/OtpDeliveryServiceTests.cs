using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UserDirectory.Contracts.Queries;

namespace Notification.Tests.Verification;

/// <summary>
/// Notification only DELIVERS one-time codes (Identity issues and verifies them): channel resolution, contact
/// checks and failures reported as results, never exceptions.
/// </summary>
public sealed class OtpDeliveryServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeChannelAdapter _whatsApp = new(NotificationChannel.WhatsApp);
    private readonly FakeChannelAdapter _email = new(NotificationChannel.Email);
    private readonly Mock<INotificationPreferenceRepository> _preferences = new();
    private UserContactInfoDto _contact = new("user@example.com", "+201001234567");

    private OtpDeliveryService CreateService()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetUserContactInfoQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _contact);

        return new OtpDeliveryService(
            sender.Object,
            new INotificationChannelAdapter[] { _whatsApp, _email },
            _preferences.Object,
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions()),
            NullLogger<OtpDeliveryService>.Instance);
    }

    [Fact]
    public async Task Phone_verification_code_is_delivered_over_whatsapp_with_the_given_code()
    {
        var result = await CreateService().SendAsync(UserId, VerificationPurpose.PhoneVerification, "123456");

        result.Success.Should().BeTrue();
        result.Channel.Should().Be(NotificationChannel.WhatsApp);
        _whatsApp.Sends.Should().Be(1);
        _whatsApp.LastCode.Should().Be("123456");
    }

    [Fact]
    public async Task Missing_phone_number_is_reported_and_nothing_is_sent()
    {
        _contact = new UserContactInfoDto("user@example.com", null);

        var result = await CreateService().SendAsync(UserId, VerificationPurpose.PhoneVerification, "123456");

        result.Failure.Should().Be(OtpDeliveryFailure.MissingContact);
        _whatsApp.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Adapter_exception_becomes_a_delivery_failure_result()
    {
        _whatsApp.Failures.Enqueue(new HttpRequestException("down"));

        var result = await CreateService().SendAsync(UserId, VerificationPurpose.PhoneVerification, "123456");

        result.Success.Should().BeFalse();
        result.Failure.Should().Be(OtpDeliveryFailure.DeliveryFailed);
    }

    [Fact]
    public async Task Email_verification_has_no_otp_email_template_yet()
    {
        var result = await CreateService().SendAsync(UserId, VerificationPurpose.EmailVerification, "123456");

        result.Channel.Should().Be(NotificationChannel.Email);
        result.Failure.Should().Be(OtpDeliveryFailure.ChannelUnavailable);
        _email.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Two_factor_code_follows_the_users_channel_preference()
    {
        _preferences.Setup(p => p.GetChannelOrderAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { NotificationChannel.WhatsApp });

        var result = await CreateService().SendAsync(UserId, VerificationPurpose.TwoFactorLogin, "654321");

        result.Success.Should().BeTrue();
        _whatsApp.LastCode.Should().Be("654321");
    }
}
