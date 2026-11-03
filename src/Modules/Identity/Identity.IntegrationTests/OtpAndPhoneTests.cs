using Identity.Contracts.Auth.Commands;
using Identity.Data;
using Identity.Domains.Auth.Features.SendPhoneVerification;
using Identity.Domains.Auth.Features.VerifyPhone;
using Identity.Domains.Users.Models;
using Identity.Resources;
using Identity.Services.Otp;
using Identity.Services.Sms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Exceptions;

namespace Identity.IntegrationTests;

[Collection(IdentityPostgresCollection.Name)]
public sealed class OtpAndPhoneTests(IdentityPostgresFixture fixture)
{
    private static string RandomPhone() => $"+2010{Random.Shared.Next(10_000_000, 99_999_999)}";

    private async Task<SendPhoneVerificationResult> SendAsync(Guid userId)
    {
        await using var scope = fixture.NewScope();
        var sp = scope.ServiceProvider;
        return await new SendPhoneVerificationHandler(
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<IOtpCodeService>(),
                sp.GetRequiredService<ISmsService>(),
                sp.GetRequiredService<IStringLocalizer<IdentityResource>>(),
                NullLogger<SendPhoneVerificationHandler>.Instance)
            .Handle(new SendPhoneVerificationCommand(userId), default);
    }

    private async Task<VerifyPhoneResult> VerifyAsync(Guid userId, string code)
    {
        await using var scope = fixture.NewScope();
        var sp = scope.ServiceProvider;
        return await new VerifyPhoneHandler(
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<IOtpCodeService>(),
                sp.GetRequiredService<IStringLocalizer<IdentityResource>>(),
                NullLogger<VerifyPhoneHandler>.Instance)
            .Handle(new VerifyPhoneCommand(userId, code), default);
    }

    private string LastCodeSentTo(string phone)
    {
        var sms = fixture.Services.GetRequiredService<RecordingSmsService>();
        lock (sms.Sent)
        {
            return sms.Sent.Last(s => s.Phone == phone).Code;
        }
    }

    private async Task<ApplicationUser> ReloadAsync(Guid id)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task Phone_verification_end_to_end_stores_only_a_hash_and_verifies_once()
    {
        var phone = RandomPhone();
        var user = await fixture.CreateUserAsync(phone);

        await SendAsync(user.Id);
        var code = LastCodeSentTo(phone);

        await using (var scope = fixture.NewScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
                .OtpCodes.AsNoTracking().SingleAsync(o => o.UserId == user.Id);
            row.CodeHash.Should().NotContain(code);
        }

        var result = await VerifyAsync(user.Id, code);

        result.IsProfileComplete.Should().BeTrue();
        (await ReloadAsync(user.Id)).IsPhoneNumberVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Attempt_limit_locks_the_code_even_for_the_right_answer()
    {
        var phone = RandomPhone();
        var user = await fixture.CreateUserAsync(phone);
        await SendAsync(user.Id);
        var code = LastCodeSentTo(phone);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++) // PhoneVerification:MaxVerificationAttempts default
        {
            await FluentActions.Invoking(() => VerifyAsync(user.Id, wrong)).Should().ThrowAsync<BadRequestException>();
        }

        await FluentActions.Invoking(() => VerifyAsync(user.Id, code)).Should().ThrowAsync<BadRequestException>();
        (await ReloadAsync(user.Id)).IsPhoneNumberVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Parallel_guesses_cannot_exceed_the_attempt_limit()
    {
        var phone = RandomPhone();
        var user = await fixture.CreateUserAsync(phone);
        await SendAsync(user.Id);

        var guesses = Enumerable.Range(0, 30).Select(i => Task.Run(async () =>
        {
            await using var scope = fixture.NewScope();
            return await scope.ServiceProvider.GetRequiredService<IOtpCodeService>()
                .VerifyAsync(user.Id, OtpPurpose.PhoneVerification, $"9{i:D5}");
        }));

        var results = await Task.WhenAll(guesses);

        results.Should().NotContain(r => r.Succeeded);
        results.Count(r => r.Status == OtpVerificationStatus.Mismatch).Should().BeLessThanOrEqualTo(5,
            "at most MaxAttempts guesses were actually compared; the rest were refused as Locked");

        await using var check = fixture.NewScope();
        var row = await check.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
            .OtpCodes.AsNoTracking().SingleAsync(o => o.UserId == user.Id && o.Purpose == OtpPurpose.PhoneVerification);
        row.Attempts.Should().Be(row.MaxAttempts, "every guess beyond the limit was rejected before comparing");
    }

    [Fact]
    public async Task Resend_inside_cooldown_is_rate_limited()
    {
        var user = await fixture.CreateUserAsync(RandomPhone());
        await SendAsync(user.Id);

        await FluentActions.Invoking(() => SendAsync(user.Id)).Should().ThrowAsync<OtpRateLimitedException>();
    }

    [Fact]
    public async Task Phone_numbers_are_unique_but_many_users_may_have_none()
    {
        var phone = RandomPhone();
        await fixture.CreateUserAsync(phone);
        await fixture.CreateUserAsync();
        await fixture.CreateUserAsync();

        await using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        db.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"dup{Guid.NewGuid():N}",
            NormalizedUserName = $"DUP{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            PhoneNumber = phone
        });

        var act = () => db.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException!.Message.Should().Contain("ix_users_phone_number");
    }
}
