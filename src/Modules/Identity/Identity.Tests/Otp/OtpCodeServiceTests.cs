using System.Security.Cryptography;
using System.Text;
using Identity.Options;
using Identity.Services.Otp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Tests.Otp;

public sealed class OtpCodeServiceTests
{
    private const string Key = "unit-test-otp-hmac-key-0123456789abcdef";
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryOtpCodeRepository _repo = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private OtpCodeService CreateService(
        int maxAttempts = 5, int cooldownSeconds = 60, int expiryMinutes = 10, int maxSendsPerDay = 10,
        int resetMaxAttempts = 3) =>
        new(
            _repo,
            Microsoft.Extensions.Options.Options.Create(new IdentitySecurityOptions { OtpHmacKey = Key }),
            Microsoft.Extensions.Options.Options.Create(new PhoneVerificationOptions
            {
                CodeLength = 6,
                CodeExpirationMinutes = expiryMinutes,
                MaxVerificationAttempts = maxAttempts,
                ResendCooldownSeconds = cooldownSeconds,
                MaxSendsPerDay = maxSendsPerDay
            }),
            Microsoft.Extensions.Options.Options.Create(new PasswordResetOptions { OtpExpiryMinutes = 15, MaxOtpAttempts = resetMaxAttempts }),
            _time,
            NullLogger<OtpCodeService>.Instance);

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task Issue_stores_only_an_hmac_of_the_code_never_the_plaintext()
    {
        var otp = await CreateService().IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var row = _repo.Rows[(UserId, OtpPurpose.PhoneVerification)];
        var expected = Convert.ToBase64String(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Key), Encoding.UTF8.GetBytes($"1:{UserId:N}:{otp.Code}")));

        row.CodeHash.Should().Be(expected);
        row.CodeHash.Should().NotContain(otp.Code);
        otp.Code.Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public void Generated_codes_are_zero_padded_digits_of_the_requested_length()
    {
        Enumerable.Range(0, 200).Select(_ => OtpCodeService.GenerateCode(6))
            .Should().OnlyContain(c => c.Length == 6 && c.All(char.IsDigit));
    }

    [Fact]
    public void Hash_is_bound_to_user_and_purpose()
    {
        var service = CreateService();
        var a = service.ComputeHash(UserId, OtpPurpose.PhoneVerification, "123456");

        service.ComputeHash(Guid.NewGuid(), OtpPurpose.PhoneVerification, "123456").Should().NotBe(a);
        service.ComputeHash(UserId, OtpPurpose.PasswordReset, "123456").Should().NotBe(a);
    }

    [Fact]
    public async Task Correct_code_succeeds_once_then_is_consumed()
    {
        var service = CreateService();
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code)).Succeeded.Should().BeTrue();
        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code)).Status
            .Should().Be(OtpVerificationStatus.NotFound);
    }

    [Fact]
    public async Task Code_of_one_purpose_does_not_verify_another()
    {
        var service = CreateService();
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        (await service.VerifyAsync(UserId, OtpPurpose.PasswordReset, otp.Code)).Status
            .Should().Be(OtpVerificationStatus.NotFound);
    }

    [Fact]
    public async Task Expired_code_is_rejected_even_when_correct()
    {
        var service = CreateService(expiryMinutes: 10);
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        _time.Advance(TimeSpan.FromMinutes(10));

        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code)).Status
            .Should().Be(OtpVerificationStatus.Expired);
    }

    [Fact]
    public async Task Max_attempts_locks_the_code_so_even_the_correct_code_fails()
    {
        var service = CreateService(maxAttempts: 3);
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var r1 = await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, WrongCode(otp.Code));
        var r2 = await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, WrongCode(otp.Code));
        var r3 = await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, WrongCode(otp.Code));

        r1.Should().Be(new OtpVerificationResult(OtpVerificationStatus.Mismatch, 2));
        r2.Should().Be(new OtpVerificationResult(OtpVerificationStatus.Mismatch, 1));
        r3.Status.Should().Be(OtpVerificationStatus.Locked);

        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code)).Status
            .Should().Be(OtpVerificationStatus.Locked);
    }

    [Fact]
    public async Task Reset_purpose_uses_the_password_reset_attempt_limit()
    {
        var service = CreateService(maxAttempts: 5, resetMaxAttempts: 2);
        var otp = await service.IssueAsync(UserId, OtpPurpose.PasswordReset);

        await service.VerifyAsync(UserId, OtpPurpose.PasswordReset, WrongCode(otp.Code));
        await service.VerifyAsync(UserId, OtpPurpose.PasswordReset, WrongCode(otp.Code));

        (await service.VerifyAsync(UserId, OtpPurpose.PasswordReset, otp.Code)).Status
            .Should().Be(OtpVerificationStatus.Locked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("12345678901234567890")]
    [InlineData("abcdef")]
    public async Task Malformed_input_takes_the_same_hmac_compare_path_and_consumes_an_attempt(string input)
    {
        var service = CreateService(maxAttempts: 5);
        await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var result = await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, input);

        result.Should().Be(new OtpVerificationResult(OtpVerificationStatus.Mismatch, 4));
        _repo.Rows[(UserId, OtpPurpose.PhoneVerification)].Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Resend_within_cooldown_is_rejected_and_allowed_after()
    {
        var service = CreateService(cooldownSeconds: 60);
        var first = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var tooSoon = () => service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
        (await tooSoon.Should().ThrowAsync<OtpRateLimitedException>())
            .Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));

        _time.Advance(TimeSpan.FromSeconds(61));
        var second = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        // The first code is replaced by the new one.
        if (first.Code != second.Code)
        {
            (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, first.Code)).Succeeded.Should().BeFalse();
        }

        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, second.Code)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Daily_send_cap_blocks_further_codes_until_the_window_rolls()
    {
        var service = CreateService(cooldownSeconds: 0, maxSendsPerDay: 3);

        for (var i = 0; i < 3; i++)
        {
            await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var act = () => service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
        await act.Should().ThrowAsync<OtpRateLimitedException>();

        _time.Advance(TimeSpan.FromDays(1));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Parallel_correct_verifies_succeed_exactly_once()
    {
        var service = CreateService(maxAttempts: 100);
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var results = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => Task.Run(() => service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code))));

        results.Count(r => r.Succeeded).Should().Be(1);
        results.Where(r => !r.Succeeded).Should().OnlyContain(r => r.Status == OtpVerificationStatus.NotFound);
    }

    [Fact]
    public async Task Parallel_wrong_guesses_never_exceed_the_attempt_limit()
    {
        var service = CreateService(maxAttempts: 5);
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, WrongCode(otp.Code)))));

        results.Should().NotContain(r => r.Succeeded);
        results.Count(r => r.Status == OtpVerificationStatus.Mismatch).Should().BeLessThanOrEqualTo(5);
        _repo.Rows[(UserId, OtpPurpose.PhoneVerification)].Attempts.Should().Be(5);
    }

    [Fact]
    public async Task Parallel_issues_pass_the_cooldown_exactly_once()
    {
        var service = CreateService(cooldownSeconds: 60);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            try
            {
                await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
                return true;
            }
            catch (OtpRateLimitedException)
            {
                return false;
            }
        })));

        results.Count(issued => issued).Should().Be(1);
    }

    [Fact]
    public async Task Cooldown_and_send_cap_report_their_reason()
    {
        var service = CreateService(cooldownSeconds: 30, maxSendsPerDay: 1);
        await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        var act = () => service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
        (await act.Should().ThrowAsync<OtpRateLimitedException>()).Which.Reason.Should().Be(OtpRateLimitReason.Cooldown);

        _time.Advance(TimeSpan.FromMinutes(1));
        (await act.Should().ThrowAsync<OtpRateLimitedException>()).Which.Reason.Should().Be(OtpRateLimitReason.SendCapReached);
    }

    [Fact]
    public async Task Successful_verify_does_not_reset_the_send_cap()
    {
        var service = CreateService(cooldownSeconds: 0, maxSendsPerDay: 2);

        for (var i = 0; i < 2; i++)
        {
            var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
            (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code)).Succeeded.Should().BeTrue();
        }

        var act = () => service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
        await act.Should().ThrowAsync<OtpRateLimitedException>();
    }

    [Fact]
    public async Task Every_purpose_is_isolated_from_every_other()
    {
        var service = CreateService(cooldownSeconds: 60, maxAttempts: 100, resetMaxAttempts: 100);
        var purposes = Enum.GetValues<OtpPurpose>();
        var codes = new Dictionary<OtpPurpose, string>();

        // Independent cooldowns: issuing one purpose does not block another.
        foreach (var purpose in purposes)
        {
            codes[purpose] = (await service.IssueAsync(UserId, purpose)).Code;
        }

        foreach (var issuedFor in purposes)
        {
            foreach (var other in purposes.Where(p => p != issuedFor && codes[p] != codes[issuedFor]))
            {
                (await service.VerifyAsync(UserId, other, codes[issuedFor])).Succeeded.Should().BeFalse(
                    $"a {issuedFor} code must not verify {other}");
            }
        }

        foreach (var purpose in purposes)
        {
            (await service.VerifyAsync(UserId, purpose, codes[purpose])).Succeeded.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Release_invalidates_the_code_and_lifts_the_cooldown()
    {
        var service = CreateService(cooldownSeconds: 60);
        var first = await service.IssueAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        await service.ReleaseAsync(UserId, OtpPurpose.OnboardingPhoneVerification, first);

        (await service.VerifyAsync(UserId, OtpPurpose.OnboardingPhoneVerification, first.Code)).Status
            .Should().Be(OtpVerificationStatus.NotFound);
        var second = await service.IssueAsync(UserId, OtpPurpose.OnboardingPhoneVerification);
        (await service.VerifyAsync(UserId, OtpPurpose.OnboardingPhoneVerification, second.Code)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Release_of_an_older_code_does_not_touch_a_newer_one()
    {
        var service = CreateService(cooldownSeconds: 0);
        var first = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);
        OtpIssueResult second;
        do
        { second = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification); } while (second.Code == first.Code);

        await service.ReleaseAsync(UserId, OtpPurpose.PhoneVerification, first);

        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, second.Code)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Invalidate_kills_the_code_but_keeps_the_cooldown()
    {
        var service = CreateService(cooldownSeconds: 60);
        var otp = await service.IssueAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        await service.InvalidateAsync(UserId, OtpPurpose.OnboardingPhoneVerification);

        (await service.VerifyAsync(UserId, OtpPurpose.OnboardingPhoneVerification, otp.Code)).Status
            .Should().Be(OtpVerificationStatus.NotFound);
        var act = () => service.IssueAsync(UserId, OtpPurpose.OnboardingPhoneVerification);
        await act.Should().ThrowAsync<OtpRateLimitedException>();
    }

    [Fact]
    public async Task Code_is_still_valid_just_before_expiry()
    {
        var service = CreateService(expiryMinutes: 10);
        var otp = await service.IssueAsync(UserId, OtpPurpose.PhoneVerification);

        _time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));

        (await service.VerifyAsync(UserId, OtpPurpose.PhoneVerification, otp.Code)).Succeeded.Should().BeTrue();
    }
}
