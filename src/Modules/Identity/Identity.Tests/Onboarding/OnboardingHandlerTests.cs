using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Identity.Contracts.Onboarding.Commands;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Features.ResendOtp;
using Identity.Domains.Onboarding.Features.VerifyPhone;
using Identity.Domains.Onboarding.Models;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Otp;
using Identity.Services.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Contracts.Enums;
using Shared.Core.Exceptions;
using Shared.Core.Web;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Identity.Tests.Onboarding;

/// <summary>
/// Phone onboarding: verify-phone is the one phone-code flow that signs the user in, so a wrong, expired or locked
/// code, a timed-out process, or losing the race with the timeout job must never issue a session. Resend honours the
/// state machine and the resend limit.
/// </summary>
public sealed class OnboardingHandlerTests
{
    private readonly Mock<IOnboardingProcessRepository> _processes = new();
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IOtpCodeService> _otp = new();
    private readonly Mock<IAuthSessionService> _sessions = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<IOtpSender> _sender = new();
    private readonly ApplicationUser _user = Mocks.User(active: true);

    public OnboardingHandlerTests()
    {
        _users.WithUsers((_user, ["User"]));
        _users.Setup(m => m.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _sessions.Setup(s => s.IssueAsync(_user, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSession("access", "refresh", DateTime.UtcNow.AddMinutes(15), ["User"]));
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("timeout-job-2");
        _sender.Setup(s => s.SendAsync(_user.Id, OtpPurpose.OnboardingPhoneVerification, It.IsAny<NotificationCulture>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpSendResult(OtpSendFailure.None));
    }

    private OnboardingProcess ProcessIn(OnboardingStatus status)
    {
        var process = OnboardingProcess.Create(_user.Id);
        if (status is OnboardingStatus.OtpSent or OnboardingStatus.TimedOut or OnboardingStatus.PhoneVerified)
            process.MarkOtpSent("timeout-job-1");
        if (status == OnboardingStatus.TimedOut)
            process.MarkTimedOut();
        if (status == OnboardingStatus.PhoneVerified)
            process.MarkPhoneVerified();
        return process;
    }

    private void Active(OnboardingProcess? process) =>
        _processes.Setup(r => r.GetActiveByUserIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(process);

    private void Latest(OnboardingProcess? process) =>
        _processes.Setup(r => r.GetLatestByUserIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(process);

    private void CodeIs(OtpVerificationStatus status) =>
        _otp.Setup(o => o.VerifyAsync(_user.Id, OtpPurpose.OnboardingPhoneVerification, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerificationResult(status, 0));

    private VerifyOnboardingPhoneHandler Verify() => new(_processes.Object, _users.Object, _otp.Object, _sessions.Object,
        Mock.Of<IHttpContextService>(), _jobs.Object, NullLogger<VerifyOnboardingPhoneHandler>.Instance);

    private ResendOnboardingOtpHandler Resend(int maxResends = 3) => new(_processes.Object, _sender.Object, _jobs.Object,
        MsOptions.Create(new OnboardingOptions { MaxResends = maxResends }), NullLogger<ResendOnboardingOtpHandler>.Instance);

    private static VerifyOnboardingPhoneCommand VerifyCommand(Guid userId) => new(userId, "123456");

    [Fact]
    public async Task Correct_code_verifies_the_phone_signs_the_user_in_and_cancels_the_timeout()
    {
        var process = ProcessIn(OnboardingStatus.OtpSent);
        Active(process);
        CodeIs(OtpVerificationStatus.Success);

        var result = await Verify().Handle(VerifyCommand(_user.Id), default);

        result.AccessToken.Should().Be("access");
        process.Status.Should().Be(OnboardingStatus.PhoneVerified);
        _user.IsPhoneNumberVerified.Should().BeTrue();
        _jobs.Verify(j => j.ChangeState("timeout-job-1", It.IsAny<DeletedState>(), It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData(OtpVerificationStatus.Mismatch, "Invalid verification code")]
    [InlineData(OtpVerificationStatus.NotFound, "Invalid verification code")]
    [InlineData(OtpVerificationStatus.Expired, "expired")]
    [InlineData(OtpVerificationStatus.Locked, "Too many failed attempts")]
    public async Task A_bad_code_issues_no_session_and_changes_nothing(OtpVerificationStatus status, string message)
    {
        var process = ProcessIn(OnboardingStatus.OtpSent);
        Active(process);
        CodeIs(status);

        var thrown = await Verify().Invoking(h => h.Handle(VerifyCommand(_user.Id), default))
            .Should().ThrowAsync<BadRequestException>();

        thrown.Which.Message.Should().Contain(message);
        process.Status.Should().Be(OnboardingStatus.OtpSent);
        _user.IsPhoneNumberVerified.Should().BeFalse();
        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_timed_out_onboarding_says_register_again()
    {
        Active(null);
        Latest(ProcessIn(OnboardingStatus.TimedOut));

        await Verify().Invoking(h => h.Handle(VerifyCommand(_user.Id), default))
            .Should().ThrowAsync<BadRequestException>().WithMessage("*register again*");
        _otp.Invocations.Should().BeEmpty("no code is consumed for a dead process");
    }

    [Fact]
    public async Task Losing_the_race_with_the_timeout_job_issues_no_session()
    {
        Active(ProcessIn(OnboardingStatus.OtpSent));
        CodeIs(OtpVerificationStatus.Success);
        _processes.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("timeout committed first"));

        await Verify().Invoking(h => h.Handle(VerifyCommand(_user.Id), default))
            .Should().ThrowAsync<BadRequestException>().WithMessage("*expired*");

        _user.IsPhoneNumberVerified.Should().BeFalse();
        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_user_or_no_process_is_not_found()
    {
        await Verify().Invoking(h => h.Handle(VerifyCommand(Guid.NewGuid()), default))
            .Should().ThrowAsync<NotFoundException>();

        Active(null);
        Latest(null);
        await Verify().Invoking(h => h.Handle(VerifyCommand(_user.Id), default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Resend_while_waiting_counts_against_the_limit()
    {
        var process = ProcessIn(OnboardingStatus.OtpSent);
        Active(process);

        var result = await Resend(maxResends: 3).Handle(new ResendOnboardingOtpCommand(_user.Id), default);

        process.ResendCount.Should().Be(1);
        result.RemainingResends.Should().Be(2);
        _sender.Verify(s => s.SendAsync(_user.Id, OtpPurpose.OnboardingPhoneVerification, It.IsAny<NotificationCulture>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resend_beyond_the_limit_is_refused_without_sending()
    {
        var process = ProcessIn(OnboardingStatus.OtpSent);
        Active(process);
        await Resend(maxResends: 1).Handle(new ResendOnboardingOtpCommand(_user.Id), default);
        _sender.Invocations.Clear();

        await Resend(maxResends: 1).Invoking(h => h.Handle(new ResendOnboardingOtpCommand(_user.Id), default))
            .Should().ThrowAsync<BadRequestException>().WithMessage("*limit*");
        _sender.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Resend_for_a_registered_process_sends_the_first_code_and_schedules_the_timeout()
    {
        var process = ProcessIn(OnboardingStatus.Registered);
        Active(process);

        await Resend().Handle(new ResendOnboardingOtpCommand(_user.Id), default);

        process.Status.Should().Be(OnboardingStatus.OtpSent);
        process.TimeoutHangfireJobId.Should().Be("timeout-job-2");
        _jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<ScheduledState>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_send_is_reported_and_does_not_burn_a_resend()
    {
        var process = ProcessIn(OnboardingStatus.OtpSent);
        Active(process);
        _sender.Setup(s => s.SendAsync(_user.Id, OtpPurpose.OnboardingPhoneVerification, It.IsAny<NotificationCulture>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpSendResult(OtpSendFailure.Cooldown, Error: "cooldown"));

        await Resend().Invoking(h => h.Handle(new ResendOnboardingOtpCommand(_user.Id), default))
            .Should().ThrowAsync<BadRequestException>();
        _processes.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the incremented count is not persisted when nothing was sent");
    }

    [Fact]
    public async Task Resend_after_the_phone_was_verified_is_refused()
    {
        Active(ProcessIn(OnboardingStatus.PhoneVerified));

        await Resend().Invoking(h => h.Handle(new ResendOnboardingOtpCommand(_user.Id), default))
            .Should().ThrowAsync<BadRequestException>();
        _sender.Invocations.Should().BeEmpty();
    }
}
