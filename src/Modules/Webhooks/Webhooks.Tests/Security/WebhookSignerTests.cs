using Webhooks.Security;

namespace Webhooks.Tests.Security;

public class WebhookSignerTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string Body = "{\"id\":\"evt_1\",\"type\":\"catalog.product-published.v1\",\"data\":{\"sku\":\"A-1\"}}";

    [Fact]
    public void Matches_the_Standard_Webhooks_reference_vector()
    {
        // The sample from https://www.standardwebhooks.com that every reference implementation is checked against.
        const string secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
        var now = DateTimeOffset.FromUnixTimeSeconds(1614265330);

        WebhookSigner.Verify(secret, "msg_p5jXN8AQM9LWM0D4loKWxJek", "1614265330", "{\"test\": 2432232314}",
            "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=", now).Should().BeTrue();
    }

    [Fact]
    public void A_generated_secret_has_the_whsec_prefix_and_32_bytes_of_key()
    {
        var secret = WebhookSigner.GenerateSecret();

        secret.Should().StartWith("whsec_");
        Convert.FromBase64String(secret["whsec_".Length..]).Should().HaveCount(32);
        WebhookSigner.GenerateSecret().Should().NotBe(secret);
    }

    [Fact]
    public void Signing_is_verifiable_by_the_receiver_side_check()
    {
        var secret = WebhookSigner.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds();

        var signature = WebhookSigner.Sign(secret, Id, timestamp, Body);

        signature.Should().StartWith("v1,");
        WebhookSigner.Verify(secret, WebhookSigner.MessageIdHeader(Id), timestamp.ToString(), Body, signature, now)
            .Should().BeTrue();
    }

    [Fact]
    public void A_tampered_body_fails_verification()
    {
        var secret = WebhookSigner.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var signature = WebhookSigner.Sign(secret, Id, ts, Body);

        WebhookSigner.Verify(secret, WebhookSigner.MessageIdHeader(Id), ts.ToString(), Body.Replace("A-1", "A-2"),
            signature, now).Should().BeFalse();
    }

    [Fact]
    public void A_different_secret_fails_verification()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var signature = WebhookSigner.Sign(WebhookSigner.GenerateSecret(), Id, ts, Body);

        WebhookSigner.Verify(WebhookSigner.GenerateSecret(), WebhookSigner.MessageIdHeader(Id), ts.ToString(), Body,
            signature, now).Should().BeFalse();
    }

    [Fact]
    public void A_different_message_id_or_timestamp_fails_verification()
    {
        var secret = WebhookSigner.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var signature = WebhookSigner.Sign(secret, Id, ts, Body);

        WebhookSigner.Verify(secret, WebhookSigner.MessageIdHeader(Guid.NewGuid()), ts.ToString(), Body, signature, now)
            .Should().BeFalse();
        WebhookSigner.Verify(secret, WebhookSigner.MessageIdHeader(Id), (ts + 1).ToString(), Body, signature, now)
            .Should().BeFalse();
    }

    [Fact]
    public void A_stale_timestamp_is_rejected_even_with_a_valid_signature()
    {
        var secret = WebhookSigner.GenerateSecret();
        var old = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ts = old.ToUnixTimeSeconds();
        var signature = WebhookSigner.Sign(secret, Id, ts, Body);

        WebhookSigner.Verify(secret, WebhookSigner.MessageIdHeader(Id), ts.ToString(), Body, signature, DateTimeOffset.UtcNow)
            .Should().BeFalse("replays outside the tolerance must be refused");
    }

    [Fact]
    public void Any_valid_signature_in_a_space_separated_list_is_enough()
    {
        var secret = WebhookSigner.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var good = WebhookSigner.Sign(secret, Id, ts, Body);

        WebhookSigner.Verify(secret, WebhookSigner.MessageIdHeader(Id), ts.ToString(), Body,
            "v1,AAAA v2,whatever not-a-signature " + good, now).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("v1,not-base64!!")]
    [InlineData("v2,AAAA")]
    public void Malformed_signature_headers_never_verify_or_throw(string header)
    {
        var now = DateTimeOffset.UtcNow;

        WebhookSigner.Verify(WebhookSigner.GenerateSecret(), "msg_x", now.ToUnixTimeSeconds().ToString(), Body, header, now)
            .Should().BeFalse();
    }
}
