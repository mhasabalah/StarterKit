using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Dispatch;

public sealed class ApnsPushProviderTests : IDisposable
{
    private static readonly string IosToken = new('a', 64);

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly StubHandler _handler = new();

    private readonly string _keyFile = Path.GetTempFileName();

    public void Dispose()
    {
        _key.Dispose();
        File.Delete(_keyFile);
    }

    private ApnsPushProvider Provider(bool sandbox = false)
    {
        File.WriteAllText(_keyFile, _key.ExportPkcs8PrivateKeyPem());
        var options = new PushOptions
        {
            Apns = new ApnsOptions
            {
                Enabled = true, KeyId = "ABC123DEFG", TeamId = "TEAM123456", BundleId = "com.example.app", UseSandbox = sandbox,
                PrivateKeyFile = _keyFile
            }
        };
        return new ApnsPushProvider(new HttpClient(_handler), MsOptions.Create(options), _clock, NullLogger<ApnsPushProvider>.Instance);
    }

    private static PushMessage Message(TimeSpan? ttl = null) => new("Hello", "World",
        new Dictionary<string, string> { ["category"] = "billing.paid", ["invoiceId"] = "7" },
        "0123456789abcdef0123456789abcdef", ttl ?? TimeSpan.FromHours(1));

    private static PushTarget Ios(string? token = null) => new(Guid.NewGuid(), token ?? IosToken, PushPlatform.Ios);

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            lock (Bodies)
            {
                Bodies.Add(body);
            }

            return Respond(request);
        }
    }

    private static HttpResponseMessage Reason(HttpStatusCode status, string reason) =>
        new(status) { Content = new StringContent($"{{\"reason\":\"{reason}\"}}", Encoding.UTF8, "application/json") };

    [Fact]
    public async Task A_send_posts_to_the_production_host_with_the_apns_headers_and_alert_payload()
    {
        var target = Ios();

        var results = await Provider().SendAsync(Message(), [target], CancellationToken.None);

        results.Should().ContainSingle(r => r.DeviceId == target.DeviceId && r.Outcome == PushSendOutcome.Delivered);
        var request = _handler.Requests.Single();
        request.RequestUri!.ToString().Should().Be($"https://api.push.apple.com/3/device/{IosToken}");
        request.Method.Should().Be(HttpMethod.Post);
        request.Version.Should().Be(HttpVersion.Version20);
        request.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionExact);
        request.Headers.GetValues("apns-topic").Should().Equal("com.example.app");
        request.Headers.GetValues("apns-push-type").Should().Equal("alert");
        request.Headers.GetValues("apns-priority").Should().Equal("10");
        request.Headers.GetValues("apns-collapse-id").Should().Equal("0123456789abcdef0123456789abcdef");
        request.Headers.GetValues("apns-expiration").Should().Equal(_clock.Now.AddHours(1).ToUnixTimeSeconds().ToString());
        request.Headers.Authorization!.Scheme.Should().Be("bearer");

        using var body = JsonDocument.Parse(_handler.Bodies.Single());
        var root = body.RootElement;
        root.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString().Should().Be("Hello");
        root.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString().Should().Be("World");
        root.GetProperty("aps").GetProperty("thread-id").GetString().Should().Be("billing.paid");
        root.GetProperty("invoiceId").GetString().Should().Be("7");
    }

    [Fact]
    public async Task The_sandbox_option_selects_the_sandbox_host()
    {
        await Provider(sandbox: true).SendAsync(Message(), [Ios()], CancellationToken.None);

        _handler.Requests.Single().RequestUri!.Host.Should().Be("api.sandbox.push.apple.com");
    }

    [Fact]
    public async Task The_provider_token_is_a_valid_ES256_jwt_with_the_key_id_team_id_and_issue_time()
    {
        await Provider().SendAsync(Message(), [Ios()], CancellationToken.None);

        var jwt = _handler.Requests.Single().Headers.Authorization!.Parameter!;
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);

        using var header = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[0])));
        header.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        header.RootElement.GetProperty("kid").GetString().Should().Be("ABC123DEFG");
        using var claims = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[1])));
        claims.RootElement.GetProperty("iss").GetString().Should().Be("TEAM123456");
        claims.RootElement.GetProperty("iat").GetInt64().Should().Be(_clock.Now.ToUnixTimeSeconds());

        var signature = Convert.FromBase64String(Pad(parts[2]));
        signature.Should().HaveCount(64, "ES256 signatures are the raw 32+32 byte r||s form");
        _key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();
    }

    [Fact]
    public async Task The_provider_token_is_cached_for_fifty_minutes_and_renewed_after()
    {
        var provider = Provider();

        await provider.SendAsync(Message(), [Ios()], CancellationToken.None);
        _clock.Now = _clock.Now.AddMinutes(49);
        await provider.SendAsync(Message(), [Ios()], CancellationToken.None);
        _clock.Now = _clock.Now.AddMinutes(2);
        await provider.SendAsync(Message(), [Ios()], CancellationToken.None);

        var tokens = _handler.Requests.Select(r => r.Headers.Authorization!.Parameter).ToList();
        tokens[1].Should().Be(tokens[0]);
        tokens[2].Should().NotBe(tokens[0]);
    }

    [Fact]
    public async Task Concurrent_sends_share_one_provider_token()
    {
        var provider = Provider();
        var targets = Enumerable.Range(0, 40).Select(i => Ios(new string((char)('a' + i % 6), 64))).ToList();

        var results = await provider.SendAsync(Message(), targets, CancellationToken.None);

        results.Should().OnlyContain(r => r.Outcome == PushSendOutcome.Delivered);
        _handler.Requests.Select(r => r.Headers.Authorization!.Parameter).Distinct().Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "BadDeviceToken", PushSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.Gone, "Unregistered", PushSendOutcome.InvalidToken)]
    [InlineData(HttpStatusCode.TooManyRequests, "TooManyRequests", PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.InternalServerError, "InternalServerError", PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable", PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.Forbidden, "ExpiredProviderToken", PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.Forbidden, "InvalidProviderToken", PushSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.BadRequest, "DeviceTokenNotForTopic", PushSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.BadRequest, "PayloadEmpty", PushSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "PayloadTooLarge", PushSendOutcome.Rejected)]
    public async Task Apns_errors_map_to_what_the_job_does_with_the_device(HttpStatusCode status, string reason, PushSendOutcome expected)
    {
        _handler.Respond = _ => Reason(status, reason);

        var results = await Provider().SendAsync(Message(), [Ios()], CancellationToken.None);

        results.Single().Outcome.Should().Be(expected);
        results.Single().Error.Should().Contain(reason);
        ApnsErrorClassifier.Classify(status, reason).Should().Be(expected);
    }

    [Fact]
    public void A_5xx_without_a_json_reason_is_still_transient_and_other_4xx_are_rejected()
    {
        ApnsErrorClassifier.Classify(HttpStatusCode.BadGateway, null).Should().Be(PushSendOutcome.Transient);
        ApnsErrorClassifier.Classify(HttpStatusCode.Unauthorized, null).Should().Be(PushSendOutcome.Rejected);
    }

    [Fact]
    public async Task An_expired_provider_token_is_replaced_on_the_next_send()
    {
        var provider = Provider();
        _handler.Respond = _ => Reason(HttpStatusCode.Forbidden, "ExpiredProviderToken");
        await provider.SendAsync(Message(), [Ios()], CancellationToken.None);

        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        _clock.Now = _clock.Now.AddSeconds(5); // a new iat, so a re-minted token differs
        await provider.SendAsync(Message(), [Ios()], CancellationToken.None);

        _handler.Requests[1].Headers.Authorization!.Parameter.Should().NotBe(_handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task A_network_failure_is_transient_not_an_exception()
    {
        _handler.Respond = _ => throw new HttpRequestException("connection reset");

        var results = await Provider().SendAsync(Message(), [Ios()], CancellationToken.None);

        results.Single().Outcome.Should().Be(PushSendOutcome.Transient);
    }

    [Fact]
    public async Task Each_device_gets_its_own_result()
    {
        var good = Ios(new string('a', 64));
        var dead = Ios(new string('b', 64));
        _handler.Respond = r => r.RequestUri!.AbsolutePath.EndsWith(dead.Token, StringComparison.Ordinal)
            ? Reason(HttpStatusCode.Gone, "Unregistered")
            : new HttpResponseMessage(HttpStatusCode.OK);

        var results = await Provider().SendAsync(Message(), [good, dead], CancellationToken.None);

        results.Single(r => r.DeviceId == good.DeviceId).Outcome.Should().Be(PushSendOutcome.Delivered);
        results.Single(r => r.DeviceId == dead.DeviceId).Outcome.Should().Be(PushSendOutcome.InvalidToken);
    }

    // ----- options --------------------------------------------------------------------------------------------------

    [Fact]
    public void Apns_options_validate_the_required_settings_and_the_key_file()
    {
        File.WriteAllText(_keyFile, _key.ExportPkcs8PrivateKeyPem());
        var notAKey = Path.GetTempFileName();
        File.WriteAllText(notAKey, "not a key");
        try
        {
            new ApnsOptions().Validate().Should().BeNull("disabled needs nothing");
            new ApnsOptions { Enabled = true, KeyId = "CHANGE_ME", TeamId = "T", BundleId = "b", PrivateKeyFile = _keyFile }.Validate().Should().NotBeNull();
            new ApnsOptions { Enabled = true, KeyId = "K", TeamId = "T", BundleId = "b" }.Validate().Should().NotBeNull("no key file");
            new ApnsOptions { Enabled = true, KeyId = "K", TeamId = "T", BundleId = "b", PrivateKeyFile = "/nope/missing.p8" }.Validate()
                .Should().NotBeNull("a missing file fails at startup");
            new ApnsOptions { Enabled = true, KeyId = "K", TeamId = "T", BundleId = "b", PrivateKeyFile = notAKey }.Validate()
                .Should().NotBeNull("an unparsable key fails at startup");
            new ApnsOptions { Enabled = true, KeyId = "K", TeamId = "T", BundleId = "b", PrivateKeyFile = _keyFile }.Validate().Should().BeNull();
        }
        finally
        {
            File.Delete(notAKey);
        }
    }

    [Fact]
    public async Task A_key_that_cannot_be_loaded_rejects_each_device_instead_of_throwing()
    {
        var options = new PushOptions { Apns = new ApnsOptions { Enabled = true, KeyId = "K", TeamId = "T", BundleId = "b", PrivateKeyFile = "/nope/missing.p8" } };
        var provider = new ApnsPushProvider(new HttpClient(_handler), MsOptions.Create(options), _clock, NullLogger<ApnsPushProvider>.Instance);

        var results = await provider.SendAsync(Message(), [Ios(), Ios()], CancellationToken.None);

        results.Should().OnlyContain(r => r.Outcome == PushSendOutcome.Rejected);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_burst_of_ExpiredProviderToken_answers_re_mints_the_token_once()
    {
        var provider = Provider();
        string? first = null;
        _handler.Respond = r =>
        {
            var token = r.Headers.Authorization!.Parameter!;
            first ??= token;
            return token == first ? Reason(HttpStatusCode.Forbidden, "ExpiredProviderToken") : new HttpResponseMessage(HttpStatusCode.OK);
        };
        var targets = Enumerable.Range(0, 60).Select(_ => Ios()).ToList();

        await provider.SendAsync(Message(), targets, CancellationToken.None);

        provider.TokenMints.Should().BeLessThanOrEqualTo(2, "late answers for the old token must not discard the fresh one");
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
        var mintsBefore = provider.TokenMints;
        await provider.SendAsync(Message(), [Ios()], CancellationToken.None);
        provider.TokenMints.Should().Be(mintsBefore, "the fresh token is still cached");
    }

    // ----- payload --------------------------------------------------------------------------------------------------

    [Fact]
    public void Non_ascii_text_is_written_as_utf8_not_escaped()
    {
        var bytes = ApnsPushProvider.BuildPayload(new PushMessage("عنوان", "مرحبا", new Dictionary<string, string>(), "c", TimeSpan.FromHours(1)));

        Encoding.UTF8.GetString(bytes).Should().Contain("عنوان").And.Contain("مرحبا");
    }

    [Fact]
    public void A_1000_character_arabic_body_is_trimmed_to_fit_4096_bytes_and_stays_valid_json()
    {
        var body = string.Concat(Enumerable.Repeat("مرحبا بكم ", 100)); // 1000 chars, ~2 bytes each
        var message = new PushMessage("Title", body + body + body + body, new Dictionary<string, string> { ["category"] = "billing.paid", ["invoiceId"] = "7" }, "c", TimeSpan.FromHours(1));

        var bytes = ApnsPushProvider.BuildPayload(message);

        bytes.Length.Should().BeLessThanOrEqualTo(ApnsPushProvider.MaxPayloadBytes);
        using var doc = JsonDocument.Parse(bytes);
        var sent = doc.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString()!;
        sent.Should().EndWith("\u2026").And.StartWith("مرحبا");
        doc.RootElement.GetProperty("invoiceId").GetString().Should().Be("7", "data is kept while the body can still shrink");
    }

    [Fact]
    public void A_small_payload_is_left_untouched_and_huge_data_is_dropped_last_resort()
    {
        var small = ApnsPushProvider.BuildPayload(new PushMessage("T", "B", new Dictionary<string, string>(), "c", TimeSpan.FromHours(1)));
        small.Length.Should().BeLessThan(200);

        var data = new Dictionary<string, string> { ["huge"] = new string('x', 6000), ["id"] = "7" };
        var bytes = ApnsPushProvider.BuildPayload(new PushMessage("T", "B", data, "c", TimeSpan.FromHours(1)));
        bytes.Length.Should().BeLessThanOrEqualTo(ApnsPushProvider.MaxPayloadBytes);
        using var doc = JsonDocument.Parse(bytes);
        doc.RootElement.TryGetProperty("huge", out _).Should().BeFalse();
        doc.RootElement.GetProperty("id").GetString().Should().Be("7");
    }

    // ----- router ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_router_sends_native_ios_tokens_to_apns_and_everything_else_to_the_fallback()
    {
        var apns = new FakePushProvider();
        var fcm = new FakePushProvider();
        var router = new RoutingPushProvider(fcm, apns);
        var nativeIos = Ios();
        var fcmIos = new PushTarget(Guid.NewGuid(), "fcm-token:APA91b-not-hex", PushPlatform.Ios);
        var android = new PushTarget(Guid.NewGuid(), new string('a', 64), PushPlatform.Android);

        var results = await router.SendAsync(Message(), [nativeIos, fcmIos, android], CancellationToken.None);

        apns.Calls.Single().Targets.Select(t => t.DeviceId).Should().Equal(nativeIos.DeviceId);
        fcm.Calls.Single().Targets.Select(t => t.DeviceId).Should().BeEquivalentTo([fcmIos.DeviceId, android.DeviceId]);
        results.Select(r => r.DeviceId).Should().Equal(nativeIos.DeviceId, fcmIos.DeviceId, android.DeviceId);
    }

    [Fact]
    public async Task Without_apns_the_router_sends_everything_to_the_fallback()
    {
        var fcm = new FakePushProvider();

        await new RoutingPushProvider(fcm, null).SendAsync(Message(), [Ios(), Ios("x")], CancellationToken.None);

        fcm.Calls.Single().Targets.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_router_skips_a_provider_that_has_no_devices()
    {
        var apns = new FakePushProvider();
        var fcm = new FakePushProvider();

        await new RoutingPushProvider(fcm, apns).SendAsync(Message(),
            [new PushTarget(Guid.NewGuid(), "web-token", PushPlatform.Web)], CancellationToken.None);

        apns.Calls.Should().BeEmpty();
        fcm.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task One_provider_failing_does_not_discard_the_other_providers_results()
    {
        var good = new FakePushProvider();
        var router = new RoutingPushProvider(good, new ThrowingProvider());
        var native = Ios();
        var web = new PushTarget(Guid.NewGuid(), "web-token", PushPlatform.Web);

        var results = await router.SendAsync(Message(), [native, web], CancellationToken.None);

        results.Single(r => r.DeviceId == native.DeviceId).Outcome.Should().Be(PushSendOutcome.Transient);
        results.Single(r => r.DeviceId == web.DeviceId).Outcome.Should().Be(PushSendOutcome.Delivered);
    }

    private sealed class ThrowingProvider : IPushProvider
    {
        public string Name => "Boom";

        public Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct) =>
            throw new InvalidOperationException("provider down");
    }

    private static string Pad(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }
}
