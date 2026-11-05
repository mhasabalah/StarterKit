using System.Net;
using System.Text.RegularExpressions;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// Forgot → reset → sign in, over real HTTP, by email (the link from the queued mail) and by phone (the OTP the SMS
/// provider was asked to send). A reset must sign out every existing session and retire the old password.
/// </summary>
[Collection(E2ECollection.Name)]
public partial class PasswordResetE2ETests(E2ETestFixture fixture)
{
    private const string Forgot = "/api/auth/forgot-password";
    private const string Reset = "/api/auth/reset-password";
    private const string VerifyOtp = "/api/auth/verify-reset-otp";

    private readonly AuthFlow _auth = new(fixture);

    private sealed record ResetGrant(Guid UserId, string ResetToken);

    [GeneratedRegex(@"reset-password\?userId=(?<user>[0-9a-fA-F-]{36})&token=(?<token>[^""\s&]+)")]
    private static partial Regex ResetLink();

    [Fact]
    public async Task Email_reset_link_sets_a_new_password_and_signs_out_every_session()
    {
        var user = await _auth.SeedUserAsync();
        var before = await _auth.LoginAsync(user.Email);

        (await _auth.PostAsync(_auth.Anonymous(), Forgot, new { identifier = user.Email })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // The link exactly as the mail carries it (the mail itself is delivered by the async notification pipeline).
        var mails = await _auth.QueuedMailsToAsync(user.Email);
        var link = mails.Select(m => ResetLink().Match(m)).Single(m => m.Success);
        Guid.Parse(link.Groups["user"].Value).Should().Be(user.Id);
        var token = Uri.UnescapeDataString(link.Groups["token"].Value);

        (await _auth.PostAsync(_auth.Anonymous(), Reset,
                new { userId = user.Id, token, newPassword = AuthFlow.NewPassword, confirmPassword = AuthFlow.NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertPasswordReplacedAndSessionsEndedAsync(user, before);

        // The link is single use: the password change rotated the security stamp the token is bound to.
        (await _auth.PostAsync(_auth.Anonymous(), Reset,
                new { userId = user.Id, token, newPassword = "E2e!Th1rdPassw0rd", confirmPassword = "E2e!Th1rdPassw0rd" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Phone_reset_code_verifies_then_sets_a_new_password()
    {
        var user = await _auth.SeedUserAsync(withVerifiedPhone: true);
        var before = await _auth.LoginAsync(user.Email);
        var sms = fixture.Factory.Services.GetRequiredService<E2ESmsOutbox>();

        (await _auth.PostAsync(_auth.Anonymous(), Forgot, new { identifier = user.Phone })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var code = sms.LastCodeFor(user.Phone!);
        code.Should().NotBeNullOrEmpty("forgot-password by a verified phone sends a reset code");

        // A wrong code is rejected and does not consume the right one.
        (await _auth.PostAsync(_auth.Anonymous(), VerifyOtp, new { phoneNumber = user.Phone, otpCode = WrongCode(code!) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var grant = await _auth.PostOkAsync<ResetGrant>(_auth.Anonymous(), VerifyOtp, new { phoneNumber = user.Phone, otpCode = code });
        grant.UserId.Should().Be(user.Id);

        // The code is single use.
        (await _auth.PostAsync(_auth.Anonymous(), VerifyOtp, new { phoneNumber = user.Phone, otpCode = code }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _auth.PostAsync(_auth.Anonymous(), Reset,
                new { userId = user.Id, token = grant.ResetToken, newPassword = AuthFlow.NewPassword, confirmPassword = AuthFlow.NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertPasswordReplacedAndSessionsEndedAsync(user, before);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_for_unknown_and_known_accounts()
    {
        var user = await _auth.SeedUserAsync();

        var known = await _auth.PostOkAsync<object>(_auth.Anonymous(), Forgot, new { identifier = user.Email });
        var unknown = await _auth.PostOkAsync<object>(_auth.Anonymous(), Forgot, new { identifier = $"nobody-{Guid.NewGuid():N}@example.com" });
        var unknownPhone = await _auth.PostOkAsync<object>(_auth.Anonymous(), Forgot, new { identifier = "+201099999999" });

        known.ToString().Should().Be(unknown.ToString()).And.Be(unknownPhone.ToString());
        (await _auth.QueuedMailsToAsync(user.Email)).Should().ContainSingle();
    }

    [Fact]
    public async Task Forged_or_foreign_reset_tokens_change_nothing()
    {
        var user = await _auth.SeedUserAsync();
        var session = await _auth.LoginAsync(user.Email);

        (await _auth.PostAsync(_auth.Anonymous(), Reset,
                new { userId = user.Id, token = "forged-token", newPassword = AuthFlow.NewPassword, confirmPassword = AuthFlow.NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _auth.PostAsync(_auth.Anonymous(), Reset,
                new { userId = Guid.NewGuid(), token = "forged-token", newPassword = AuthFlow.NewPassword, confirmPassword = AuthFlow.NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _auth.ProbeAsync(session.AccessToken)).Should().Be(HttpStatusCode.OK, "a failed reset revokes nothing");
        await _auth.LoginAsync(user.Email);
    }

    private async Task AssertPasswordReplacedAndSessionsEndedAsync(AuthFlow.SeededUser user, AuthFlow.Tokens before)
    {
        (await _auth.ProbeAsync(before.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await _auth.TryRefreshAsync(before.RefreshToken)).Should().Be(HttpStatusCode.Unauthorized);

        (await _auth.TryLoginAsync(user.Email, AuthFlow.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var after = await _auth.LoginAsync(user.Email, AuthFlow.NewPassword);
        (await _auth.ProbeAsync(after.AccessToken)).Should().Be(HttpStatusCode.OK);
    }

    private static string WrongCode(string code) =>
        new(code.Select(c => char.IsDigit(c) ? (char)('0' + ((c - '0' + 1) % 10)) : c).ToArray());
}
