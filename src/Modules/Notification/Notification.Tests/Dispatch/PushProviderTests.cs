using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Notification.Tests.Dispatch;

public sealed class PushProviderTests
{
    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, PushSendOutcome.InvalidToken)]
    [InlineData(MessagingErrorCode.SenderIdMismatch, PushSendOutcome.InvalidToken)]
    [InlineData(MessagingErrorCode.Unavailable, PushSendOutcome.Transient)]
    [InlineData(MessagingErrorCode.Internal, PushSendOutcome.Transient)]
    [InlineData(MessagingErrorCode.QuotaExceeded, PushSendOutcome.Transient)]
    [InlineData(MessagingErrorCode.InvalidArgument, PushSendOutcome.Rejected)]
    [InlineData(MessagingErrorCode.ThirdPartyAuthError, PushSendOutcome.Rejected)]
    public void Fcm_messaging_errors_map_to_what_the_job_does_with_the_device(MessagingErrorCode code, PushSendOutcome expected) =>
        FcmErrorClassifier.Classify(code, ErrorCode.Unknown).Should().Be(expected);

    [Theory]
    [InlineData(ErrorCode.Unavailable, PushSendOutcome.Transient)]
    [InlineData(ErrorCode.ResourceExhausted, PushSendOutcome.Transient)]
    [InlineData(ErrorCode.DeadlineExceeded, PushSendOutcome.Transient)]
    [InlineData(ErrorCode.NotFound, PushSendOutcome.InvalidToken)]
    [InlineData(ErrorCode.PermissionDenied, PushSendOutcome.Rejected)]
    [InlineData(ErrorCode.Unauthenticated, PushSendOutcome.Rejected)]
    public void Without_a_messaging_code_the_general_error_code_decides(ErrorCode code, PushSendOutcome expected) =>
        FcmErrorClassifier.Classify(null, code).Should().Be(expected);

    [Fact]
    public void The_fcm_message_collapses_on_every_platform_and_keeps_title_body_and_data()
    {
        var collapse = Guid.NewGuid().ToString("N");
        var message = new PushMessage("Title", "Body", new Dictionary<string, string> { ["category"] = "billing.paid", ["id"] = "7" },
            collapse, TimeSpan.FromHours(2));
        var targets = new[]
        {
            new PushTarget(Guid.NewGuid(), "token-a", PushPlatform.Android),
            new PushTarget(Guid.NewGuid(), "token-b", PushPlatform.Ios)
        };

        var fcm = FcmPushProvider.Build(message, targets);

        fcm.Tokens.Should().Equal("token-a", "token-b");
        fcm.Notification.Title.Should().Be("Title");
        fcm.Notification.Body.Should().Be("Body");
        fcm.Data.Should().Contain("id", "7");
        fcm.Android.Notification.Tag.Should().Be(collapse);
        fcm.Android.CollapseKey.Should().Be(collapse);
        fcm.Android.TimeToLive.Should().Be(TimeSpan.FromHours(2));
        fcm.Apns.Headers.Should().Contain("apns-collapse-id", collapse);
        fcm.Apns.Aps.ThreadId.Should().Be("billing.paid");
        fcm.Webpush.Notification.Tag.Should().Be(collapse);
        fcm.Webpush.Headers.Should().Contain("TTL", "7200");
    }

    [Fact]
    public async Task NoOp_reports_every_device_delivered_without_sending()
    {
        var targets = new[] { new PushTarget(Guid.NewGuid(), "secret-token-123456789", PushPlatform.Web) };

        var results = await new NoOpPushProvider(NullLogger<NoOpPushProvider>.Instance)
            .SendAsync(new PushMessage("t", "b", new Dictionary<string, string>(), "c", TimeSpan.FromMinutes(1)), targets, default);

        results.Should().ContainSingle(r => r.DeviceId == targets[0].DeviceId && r.Outcome == PushSendOutcome.Delivered);
    }

    [Fact]
    public async Task Fcm_refuses_a_non_service_account_credential()
    {
        // External-account / authorized-user JSON could make the SDK call arbitrary token endpoints: only a service
        // account key is accepted.
        using var provider = new FcmPushProvider(Microsoft.Extensions.Options.Options.Create(new PushOptions
        {
            Provider = PushProvider.Fcm,
            CredentialsJson = """{"type":"external_account","audience":"x","token_url":"https://evil.test/token","credential_source":{"url":"https://evil.test"}}"""
        }), NullLogger<FcmPushProvider>.Instance);

        var send = () => provider.SendAsync(new PushMessage("t", "b", new Dictionary<string, string>(), "c", TimeSpan.FromMinutes(1)),
            [new PushTarget(Guid.NewGuid(), "x", PushPlatform.Android)], default);

        await send.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("abc", "***")]
    [InlineData("0123456789abcdef", "...89abcdef")]
    public void Device_tokens_are_only_ever_shown_masked(string token, string hint) =>
        PushDevice.Hint(token).Should().Be(hint);

    [Fact]
    public void Retry_delays_follow_the_configured_schedule_and_repeat_the_last_value()
    {
        var options = new PushOptions { RetryDelaysSeconds = [30, 120] };

        options.RetryDelay(2).Should().Be(TimeSpan.FromSeconds(30));
        options.RetryDelay(3).Should().Be(TimeSpan.FromSeconds(120));
        options.RetryDelay(9).Should().Be(TimeSpan.FromSeconds(120));
        new PushOptions { RetryDelaysSeconds = [] }.RetryDelay(2).Should().Be(TimeSpan.FromSeconds(60));
    }
}
