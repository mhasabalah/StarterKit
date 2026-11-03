using Identity.Options;
using Identity.Services.Otp;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Contracts;
using Notification.Contracts.Enums;

namespace Identity.Tests.Otp;

/// <summary>Identity issues the code, Notification only delivers it; failed deliveries must not lock the user out.</summary>
public sealed class OtpSenderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryOtpCodeRepository _repo = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IOtpDeliveryService> _delivery = new();
    private readonly OtpCodeService _codes;
    private readonly OtpSender _sender;
    private string? _deliveredCode;

    public OtpSenderTests()
    {
        _codes = new OtpCodeService(
            _repo,
            Microsoft.Extensions.Options.Options.Create(new IdentitySecurityOptions
            {
                OtpHmacKey = "unit-test-otp-hmac-key-0123456789abcdef"
            }),
            Microsoft.Extensions.Options.Options.Create(new PhoneVerificationOptions { ResendCooldownSeconds = 60 }),
            Microsoft.Extensions.Options.Options.Create(new PasswordResetOptions()),
            _time,
            NullLogger<OtpCodeService>.Instance);

        _sender = new OtpSender(_codes, _delivery.Object, NullLogger<OtpSender>.Instance);
        DeliverySucceeds();
    }

    private void DeliverySucceeds() =>
        _delivery.Setup(d => d.SendAsync(UserId, It.IsAny<VerificationPurpose>(), It.IsAny<string>(),
                It.IsAny<NotificationCulture>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, VerificationPurpose, string, NotificationCulture, CancellationToken>(
                (_, _, code, _, _) => _deliveredCode = code)
            .ReturnsAsync(new OtpDeliveryResult(NotificationChannel.WhatsApp));

    private void DeliveryFails(OtpDeliveryFailure failure) =>
        _delivery.Setup(d => d.SendAsync(UserId, It.IsAny<VerificationPurpose>(), It.IsAny<string>(),
                It.IsAny<NotificationCulture>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, VerificationPurpose, string, NotificationCulture, CancellationToken>(
                (_, _, code, _, _) => _deliveredCode = code)
            .ReturnsAsync(OtpDeliveryResult.Failed(NotificationChannel.WhatsApp, failure, "nope"));

    [Fact]
    public async Task Onboarding_code_is_delivered_as_a_phone_verification_and_verifies_for_onboarding_only()
    {
        var result = await _sender.SendAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        result.Success.Should().BeTrue();
        result.Channel.Should().Be(NotificationChannel.WhatsApp);
        _delivery.Verify(d => d.SendAsync(UserId, VerificationPurpose.PhoneVerification, _deliveredCode!,
            NotificationCulture.En, It.IsAny<CancellationToken>()), Times.Once);

        (await _codes.VerifyAsync(UserId, OtpPurpose.PhoneVerification, _deliveredCode!)).Succeeded.Should().BeFalse();
        (await _codes.VerifyAsync(UserId, OtpPurpose.OnboardingPhoneVerification, _deliveredCode!)).Succeeded
            .Should().BeTrue();
    }

    [Fact]
    public async Task Failed_delivery_releases_the_code_and_the_cooldown()
    {
        DeliveryFails(OtpDeliveryFailure.DeliveryFailed);

        var failed = await _sender.SendAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        failed.Failure.Should().Be(OtpSendFailure.DeliveryFailed);
        (await _codes.VerifyAsync(UserId, OtpPurpose.OnboardingPhoneVerification, _deliveredCode!)).Status
            .Should().Be(OtpVerificationStatus.NotFound, "an undelivered code must not stay valid");

        DeliverySucceeds();
        (await _sender.SendAsync(UserId, OtpPurpose.OnboardingPhoneVerification)).Success
            .Should().BeTrue("the cooldown of an undelivered code is lifted");
    }

    [Fact]
    public async Task Missing_contact_is_reported()
    {
        DeliveryFails(OtpDeliveryFailure.MissingContact);

        (await _sender.SendAsync(UserId, OtpPurpose.PhoneVerification)).Failure.Should().Be(OtpSendFailure.MissingContact);
    }

    [Fact]
    public async Task Cooldown_is_a_result_with_retry_after_and_does_not_reach_the_channel()
    {
        await _sender.SendAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        var again = await _sender.SendAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        again.Failure.Should().Be(OtpSendFailure.Cooldown);
        again.IsRateLimited.Should().BeTrue();
        again.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
        _delivery.Verify(d => d.SendAsync(It.IsAny<Guid>(), It.IsAny<VerificationPurpose>(), It.IsAny<string>(),
            It.IsAny<NotificationCulture>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Password_reset_is_sms_only_and_cannot_go_through_notification()
    {
        var act = () => _sender.SendAsync(UserId, OtpPurpose.PasswordReset);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
