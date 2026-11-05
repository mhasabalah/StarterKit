using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.BotProtection;

namespace Shared.Core.Tests.BotProtection;

public class TurnstileHumanVerifierTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (TurnstileHumanVerifier Verifier, FakeHandler Handler) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        Action<BotProtectionOptions>? configure = null)
    {
        var options = new BotProtectionOptions { Enabled = true, SecretKey = "secret", TimeoutSeconds = 1 };
        configure?.Invoke(options);

        var meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        var handler = new FakeHandler(respond);
        var verifier = new TurnstileHumanVerifier(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(options),
            new BotProtectionMetrics(meters),
            NullLogger<TurnstileHumanVerifier>.Instance);
        return (verifier, handler);
    }

    private static (TurnstileHumanVerifier Verifier, FakeHandler Handler) CreateReturning(
        string json, Action<BotProtectionOptions>? configure = null) =>
        Create((_, _) => Task.FromResult(Json(json)), configure);

    [Fact]
    public async Task Verify_WithSuccessfulResponse_Passes_AndPostsSecretTokenAndRemoteIp()
    {
        var (verifier, handler) = CreateReturning("""{"success":true,"hostname":"app.example.com","action":"login"}""");

        var result = await verifier.VerifyAsync("tok", "203.0.113.9", "login", CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.Calls.Should().Be(1);
        handler.LastBody.Should().Contain("secret=secret").And.Contain("response=tok")
            .And.Contain("remoteip=203.0.113.9").And.Contain("idempotency_key=");
    }

    [Fact]
    public async Task Verify_WithErrorCodes_IsRejected_AndSurfacesTheCodes()
    {
        var (verifier, _) = CreateReturning("""{"success":false,"error-codes":["timeout-or-duplicate"]}""");

        var result = await verifier.VerifyAsync("tok", null, "login", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ProviderUnavailable.Should().BeFalse();
        result.ErrorCodes.Should().Equal("timeout-or-duplicate");
    }

    [Fact]
    public async Task Verify_WithHostnameOutsideAllowList_IsRejected()
    {
        var (verifier, _) = CreateReturning("""{"success":true,"hostname":"evil.example.net"}""",
            o => o.AllowedHostnames = ["app.example.com"]);

        var result = await verifier.VerifyAsync("tok", null, "login", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCodes.Should().Contain("hostname-mismatch");
    }

    [Fact]
    public async Task Verify_WithHostnameInAllowList_Passes()
    {
        var (verifier, _) = CreateReturning("""{"success":true,"hostname":"APP.example.com"}""",
            o => o.AllowedHostnames = ["app.example.com"]);

        (await verifier.VerifyAsync("tok", null, "login", CancellationToken.None)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_WithActionMismatch_IsRejected()
    {
        var (verifier, _) = CreateReturning("""{"success":true,"action":"register"}""");

        var result = await verifier.VerifyAsync("tok", null, "login", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCodes.Should().Contain("action-mismatch");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Verify_WithoutToken_IsRejectedAsMissing_AndMakesNoHttpCall(string? token)
    {
        var (verifier, handler) = CreateReturning("""{"success":true}""");

        var result = await verifier.VerifyAsync(token, null, "login", CancellationToken.None);

        result.IsMissingToken.Should().BeTrue();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Verify_WithOversizedToken_IsRejected_AndMakesNoHttpCall()
    {
        var (verifier, handler) = CreateReturning("""{"success":true}""");

        var result = await verifier.VerifyAsync(new string('x', TurnstileHumanVerifier.MaxTokenLength + 1), null, "login", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.IsMissingToken.Should().BeFalse();
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_WhenProviderTimesOut_FollowsFailOpen(bool failOpen)
    {
        var (verifier, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("""{"success":true}""");
        }, o => o.FailOpen = failOpen);

        var result = await verifier.VerifyAsync("tok", null, "login", CancellationToken.None);

        result.ProviderUnavailable.Should().BeTrue();
        result.Success.Should().Be(failOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_WhenProviderReturns5xxOrThrows_FollowsFailOpen(bool failOpen)
    {
        var (server5xx, _) = Create((_, _) => Task.FromResult(Json("{}", HttpStatusCode.BadGateway)), o => o.FailOpen = failOpen);
        var (network, _) = Create((_, _) => throw new HttpRequestException("down"), o => o.FailOpen = failOpen);

        var fromStatus = await server5xx.VerifyAsync("tok", null, "login", CancellationToken.None);
        var fromException = await network.VerifyAsync("tok", null, "login", CancellationToken.None);

        fromStatus.Success.Should().Be(failOpen);
        fromException.Success.Should().Be(failOpen);
        fromStatus.ProviderUnavailable.Should().BeTrue();
        fromException.ProviderUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_WhenCallerCancels_PropagatesCancellation_InsteadOfFailingOpen()
    {
        var (verifier, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("""{"success":true}""");
        }, o => o.TimeoutSeconds = 30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => verifier.VerifyAsync("tok", null, "login", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Verify_RecordsOutcomeMetric_ForSuccessFailureAndProviderError()
    {
        var recorded = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BotProtectionMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "outcome")
                    recorded.Add((string)tag.Value!);
        });
        listener.Start();

        await CreateReturning("""{"success":true}""").Verifier.VerifyAsync("t", null, "a", CancellationToken.None);
        await CreateReturning("""{"success":false}""").Verifier.VerifyAsync("t", null, "a", CancellationToken.None);
        await Create((_, _) => throw new HttpRequestException("down")).Verifier.VerifyAsync("t", null, "a", CancellationToken.None);

        recorded.Should().Equal("success", "failure", "provider_error");
    }

    [Fact]
    public async Task NoOpVerifier_AlwaysPasses()
    {
        var result = await new NoOpHumanVerifier().VerifyAsync(null, null, "login", CancellationToken.None);

        result.Success.Should().BeTrue();
    }
}
