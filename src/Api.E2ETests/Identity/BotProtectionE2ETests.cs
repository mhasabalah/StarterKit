using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.BotProtection;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// Bot protection over real HTTP: the protected anonymous Identity endpoints reject requests without a valid
/// <c>X-Captcha-Token</c> (the provider is a fake <see cref="IHumanVerifier"/>; the Turnstile wire format is covered
/// by Shared.Core.Tests).
/// </summary>
[Collection(E2ECollection.Name)]
public class BotProtectionE2ETests(E2ETestFixture fixture)
{
    private const string GoodToken = "good-token";
    private const string Password = "E2e!Passw0rd";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ErrorEnvelope(bool IsSuccess, int StatusCode, string? ErrorCode);

    private sealed class FakeHumanVerifier : IHumanVerifier
    {
        public Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken cancellationToken) =>
            Task.FromResult(token switch
            {
                null => HumanVerificationResult.Failed(HumanVerificationResult.MissingTokenCode),
                GoodToken => HumanVerificationResult.Passed,
                _ => HumanVerificationResult.Failed("invalid-input-response")
            });
    }

    private HttpClient CreateProtectedClient() => fixture.Factory
        .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHumanVerifier>();
            services.AddSingleton<IHumanVerifier, FakeHumanVerifier>();
        }))
        .CreateClient();

    private static async Task<ErrorEnvelope> ReadErrorAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<ErrorEnvelope>(await response.Content.ReadAsStringAsync(), Json)!;

    private static HttpRequestMessage Post(string url, object body, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Add(BotProtectionExtensions.TokenHeader, token);
        return request;
    }

    private static object LoginBody => new { identifier = "nobody@example.com", password = Password };

    private static object RegisterBody(string email) =>
        new { identifier = email, password = Password, confirmPassword = Password, firstName = "Bot", lastName = "Test" };

    [Fact]
    public async Task Login_WithoutCaptchaToken_IsRejected_WithCaptchaRequired()
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post("/api/auth/login", LoginBody, token: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).ErrorCode.Should().Be("captcha_required");
    }

    [Fact]
    public async Task Login_WithInvalidCaptchaToken_IsRejected_WithCaptchaInvalid()
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post("/api/auth/login", LoginBody, "forged"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).ErrorCode.Should().Be("captcha_invalid");
    }

    [Fact]
    public async Task Login_WithValidCaptchaToken_ReachesTheHandler()
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post("/api/auth/login", LoginBody, GoodToken));

        // Unknown account: the handler rejects the credentials; what matters is that the captcha did not.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        ((await ReadErrorAsync(response)).ErrorCode ?? string.Empty).Should().NotStartWith("captcha_");
    }

    [Fact]
    public async Task Register_WithoutCaptchaToken_IsRejected_AndCreatesNoAccount()
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post("/api/auth/register", RegisterBody($"bot-{Guid.NewGuid():N}@example.com"), token: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).ErrorCode.Should().Be("captcha_required");
    }

    [Fact]
    public async Task Register_WithValidCaptchaToken_Succeeds()
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post("/api/auth/register", RegisterBody($"human-{Guid.NewGuid():N}@example.com"), GoodToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/auth/forgot-password")]
    [InlineData("/api/identity/onboarding/register")]
    [InlineData("/api/identity/onboarding/resend-otp")]
    public async Task OtherProtectedEndpoints_WithoutCaptchaToken_AreRejected(string url)
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post(url, new { identifier = "x@example.com", phoneNumber = "+201000000000" }, token: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).ErrorCode.Should().Be("captcha_required");
    }

    [Fact]
    public async Task RefreshToken_IsNotCaptchaProtected()
    {
        using var client = CreateProtectedClient();

        var response = await client.SendAsync(Post("/api/auth/refresh", new { refreshToken = "nope" }, token: null));

        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("captcha_");
    }
}
