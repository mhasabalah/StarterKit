using Identity.Data;
using Identity.Data.Repository.Otp;
using Identity.Options;
using Identity.Services.Otp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Identity.IntegrationTests;

/// <summary>
/// The atomic guarantees of the unified OTP service against real PostgreSQL: every request uses its own scope
/// (own DbContext / connection), exactly like parallel HTTP requests.
/// </summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class UnifiedOtpPostgresTests(IdentityPostgresFixture fixture)
{
    private IOtpCodeService Service(IServiceProvider sp, int cooldownSeconds = 60, int maxAttempts = 5, int maxSends = 10) =>
        new OtpCodeService(
            sp.GetRequiredService<IOtpCodeRepository>(),
            Microsoft.Extensions.Options.Options.Create(new IdentitySecurityOptions { OtpHmacKey = IdentityPostgresFixture.OtpKey }),
            Microsoft.Extensions.Options.Options.Create(new PhoneVerificationOptions
            {
                ResendCooldownSeconds = cooldownSeconds,
                MaxVerificationAttempts = maxAttempts,
                MaxSendsPerDay = maxSends
            }),
            Microsoft.Extensions.Options.Options.Create(new PasswordResetOptions()),
            TimeProvider.System,
            NullLogger<OtpCodeService>.Instance);

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = fixture.NewScope();
        return await work(scope.ServiceProvider);
    }

    private static async Task<bool> TryIssue(IOtpCodeService service, Guid userId, OtpPurpose purpose)
    {
        try
        {
            await service.IssueAsync(userId, purpose);
            return true;
        }
        catch (OtpRateLimitedException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Parallel_correct_verifies_succeed_exactly_once()
    {
        var user = await fixture.CreateUserAsync();
        var otp = await InScope(sp => Service(sp, maxAttempts: 100).IssueAsync(user.Id, OtpPurpose.OnboardingPhoneVerification));

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(() =>
            InScope(sp => Service(sp, maxAttempts: 100).VerifyAsync(user.Id, OtpPurpose.OnboardingPhoneVerification, otp.Code)))));

        results.Count(r => r.Succeeded).Should().Be(1);
        results.Where(r => !r.Succeeded).Should().OnlyContain(r => r.Status == OtpVerificationStatus.NotFound);
    }

    [Fact]
    public async Task Parallel_issues_pass_the_cooldown_exactly_once_including_the_first_insert()
    {
        var user = await fixture.CreateUserAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            InScope(sp => TryIssue(Service(sp), user.Id, OtpPurpose.PhoneVerification)))));

        results.Count(issued => issued).Should().Be(1);
    }

    [Fact]
    public async Task Parallel_issues_without_cooldown_never_exceed_the_send_cap()
    {
        var user = await fixture.CreateUserAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            InScope(sp => TryIssue(Service(sp, cooldownSeconds: 0, maxSends: 3), user.Id, OtpPurpose.PhoneVerification)))));

        // Each request uses its own clock reading: one whose "now" is microseconds older than the latest LastSentAt
        // is (correctly) refused by the cooldown gate even at 0s, so the burst may issue fewer than the cap - but
        // never more. Sequential top-ups then prove the cap is reached exactly.
        var parallelIssued = results.Count(issued => issued);
        parallelIssued.Should().BeInRange(1, 3);

        var sequentialIssued = 0;
        for (var i = 0; i < 5; i++)
        {
            if (await InScope(sp =>
                    TryIssue(Service(sp, cooldownSeconds: 0, maxSends: 3), user.Id, OtpPurpose.PhoneVerification)))
            {
                sequentialIssued++;
            }
        }

        (parallelIssued + sequentialIssued).Should().Be(3);

        await using var check = fixture.NewScope();
        var row = await check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
            .OtpCodes.AsNoTracking().SingleAsync(o => o.UserId == user.Id && o.Purpose == OtpPurpose.PhoneVerification);
        row.SendCount.Should().Be(3);
    }

    [Fact]
    public async Task New_code_invalidates_the_previous_one()
    {
        var user = await fixture.CreateUserAsync();
        var first = await InScope(sp => Service(sp, cooldownSeconds: 0).IssueAsync(user.Id, OtpPurpose.PasswordReset));
        OtpIssueResult second;
        do
        {
            second = await InScope(sp => Service(sp, cooldownSeconds: 0).IssueAsync(user.Id, OtpPurpose.PasswordReset));
        } while (second.Code == first.Code);

        (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.PasswordReset, first.Code))).Succeeded
            .Should().BeFalse();
        (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.PasswordReset, second.Code))).Succeeded
            .Should().BeTrue();
    }

    [Fact]
    public async Task Purposes_are_isolated_and_have_independent_cooldowns()
    {
        var user = await fixture.CreateUserAsync();
        var onboarding = await InScope(sp => Service(sp).IssueAsync(user.Id, OtpPurpose.OnboardingPhoneVerification));
        var phone = await InScope(sp => Service(sp).IssueAsync(user.Id, OtpPurpose.PhoneVerification));

        if (onboarding.Code != phone.Code)
        {
            (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.PhoneVerification, onboarding.Code)))
                .Succeeded.Should().BeFalse();
        }

        (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.OnboardingPhoneVerification, onboarding.Code)))
            .Succeeded.Should().BeTrue();
        (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.PhoneVerification, phone.Code)))
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Release_and_invalidate_translate_to_sql()
    {
        var user = await fixture.CreateUserAsync();
        var issued = await InScope(sp => Service(sp).IssueAsync(user.Id, OtpPurpose.OnboardingPhoneVerification));

        await InScope(async sp =>
        {
            await Service(sp).ReleaseAsync(user.Id, OtpPurpose.OnboardingPhoneVerification, issued);
            return true;
        });

        (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.OnboardingPhoneVerification, issued.Code)))
            .Status.Should().Be(OtpVerificationStatus.NotFound);

        var reissued = await InScope(sp => Service(sp).IssueAsync(user.Id, OtpPurpose.OnboardingPhoneVerification));
        await InScope(async sp =>
        {
            await Service(sp).InvalidateAsync(user.Id, OtpPurpose.OnboardingPhoneVerification);
            return true;
        });

        (await InScope(sp => Service(sp).VerifyAsync(user.Id, OtpPurpose.OnboardingPhoneVerification, reissued.Code)))
            .Status.Should().Be(OtpVerificationStatus.NotFound);
        (await InScope(sp => TryIssue(Service(sp), user.Id, OtpPurpose.OnboardingPhoneVerification)))
            .Should().BeFalse("invalidating keeps the resend cooldown");
    }
}
