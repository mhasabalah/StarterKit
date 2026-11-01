using Identity.Contracts.Onboarding.Events;

namespace Identity.Domains.Onboarding.Models;

public class OnboardingProcess : Aggregate<Guid>
{
    public Guid UserId { get; private set; }
    public OnboardingStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? OtpSentAt { get; private set; }
    public DateTime? PhoneVerifiedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? TimedOutAt { get; private set; }
    public string? TimeoutHangfireJobId { get; private set; }
    public int ResendCount { get; private set; }
    public new DateTime CreatedAt { get; private set; }

    private OnboardingProcess()
    {
    }

    public static OnboardingProcess Create(Guid userId)
    {
        var process = new OnboardingProcess
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = OnboardingStatus.Registered,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ResendCount = 0
        };

        process.AddDomainEvent(new UserRegisteredEvent(userId));
        return process;
    }

    public void MarkOtpSent(string hangfireJobId)
    {
        if (Status != OnboardingStatus.Registered)
        {
            throw new BadRequestException("OTP can only be sent when onboarding is in Registered state.");
        }

        Status = OnboardingStatus.OtpSent;
        OtpSentAt = DateTime.UtcNow;
        TimeoutHangfireJobId = hangfireJobId;
    }

    public void MarkPhoneVerified()
    {
        if (Status != OnboardingStatus.OtpSent)
        {
            throw new BadRequestException("Phone can only be verified when onboarding is in OtpSent state.");
        }

        Status = OnboardingStatus.PhoneVerified;
        PhoneVerifiedAt = DateTime.UtcNow;
        AddDomainEvent(new PhoneVerifiedEvent(UserId));
    }

    public void MarkComplete()
    {
        if (Status != OnboardingStatus.PhoneVerified)
        {
            throw new BadRequestException("Onboarding can only be completed when phone is verified.");
        }

        Status = OnboardingStatus.Complete;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkTimedOut()
    {
        if (Status != OnboardingStatus.OtpSent)
        {
            throw new BadRequestException("Onboarding can only time out when waiting for OTP verification.");
        }

        Status = OnboardingStatus.TimedOut;
        TimedOutAt = DateTime.UtcNow;
        AddDomainEvent(new OnboardingTimedOutEvent(UserId));
    }

    public void IncrementResendCount(int maxResends)
    {
        if (Status != OnboardingStatus.OtpSent)
        {
            throw new BadRequestException("OTP can only be resent when onboarding is in OtpSent state.");
        }

        if (maxResends <= 0)
        {
            maxResends = 3;
        }

        if (ResendCount >= maxResends)
        {
            throw new BadRequestException($"Resend limit reached. Maximum {maxResends} resends allowed.");
        }

        ResendCount++;
    }
}
